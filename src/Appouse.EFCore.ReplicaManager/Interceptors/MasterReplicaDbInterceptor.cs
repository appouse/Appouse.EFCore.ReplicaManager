using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// The mechanism that makes master/replica splitting transparent: immediately before EF Core opens a
/// <see cref="DbConnection"/>, this interceptor rewrites
/// <see cref="DbConnection.ConnectionString"/> to the master or to a replica, according to the
/// ambient <see cref="IDbTargetContext.CurrentTarget"/>. When a replica is unreachable it opens the
/// connection itself, walking the remaining replicas until one answers.
/// <para>
/// TR: Master/replica ayrımını şeffaf kılan mekanizma: EF Core bir <see cref="DbConnection"/> açmadan
/// hemen önce bu interceptor, ortamdaki <see cref="IDbTargetContext.CurrentTarget"/> değerine göre
/// <see cref="DbConnection.ConnectionString"/> değerini master veya bir replica ile değiştirir. Bir
/// replica'ya erişilemediğinde bağlantıyı kendisi açar ve yanıt veren bir replica bulana kadar
/// kalanları sırayla dener.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <strong>Failover.</strong> The topology has exactly one master and any number of replicas. For a
/// replica read the interceptor asks <see cref="IReplicaSelector"/> where to start, then tries each
/// replica in turn. Replicas that <see cref="IReplicaHealthMonitor"/> currently considers unreachable
/// are moved to the back of the queue rather than excluded, so a total outage still ends in a real
/// connection attempt rather than an immediate error. Because EF Core must not see a half-opened
/// connection, the interceptor suppresses EF Core's own open and performs it, which is the only way
/// to retry a different connection string within a single operation.
/// </para>
/// <para>
/// TR: <strong>Failover.</strong> Topolojide tam olarak bir master ve istenildiği kadar replica
/// bulunur. Bir replica okuması için interceptor, nereden başlayacağını
/// <see cref="IReplicaSelector"/>'a sorar ve ardından replica'ları sırayla dener.
/// <see cref="IReplicaHealthMonitor"/>'ın o an erişilemez saydığı replica'lar dışlanmaz, sıranın
/// sonuna alınır; böylece tüm replica'lar çökmüş olsa bile süreç hemen hata vermek yerine gerçek bir
/// bağlantı denemesiyle sonuçlanır. EF Core yarım açılmış bir bağlantı görmemeli olduğundan,
/// interceptor EF Core'un kendi açma işlemini bastırıp bağlantıyı kendisi açar; tek bir işlem içinde
/// farklı bir bağlantı dizesini yeniden denemenin tek yolu budur.
/// </para>
/// <para>
/// The master path is left to EF Core: there is only one master, so there is nothing to fail over
/// to, and EF Core's own execution strategy handles its retries.
/// </para>
/// <para>
/// TR: Master yolu EF Core'a bırakılır: tek bir master olduğundan geçilecek başka bir düğüm yoktur ve
/// yeniden denemeleri EF Core'un kendi yürütme stratejisi üstlenir.
/// </para>
/// <para>
/// Registered as a <em>singleton</em>. Interceptors are captured inside <c>DbContextOptions</c>, and
/// EF Core keys its internal service-provider cache on those options: handing it a different
/// interceptor instance per scope would build a fresh internal provider per scope. Keeping the
/// interceptor stateless - all per-request state lives in <see cref="IDbTargetContext"/>'s
/// <see cref="AsyncLocal{T}"/> - is what makes the singleton lifetime correct.
/// </para>
/// <para>
/// TR: <em>Singleton</em> olarak kaydedilir. Interceptor'lar <c>DbContextOptions</c> içinde tutulur ve
/// EF Core iç servis sağlayıcı önbelleğini bu ayarlara göre anahtarlar: her scope için farklı bir
/// interceptor örneği vermek, her scope için yeni bir iç sağlayıcı kurulmasına yol açar.
/// Interceptor'ın durumsuz kalması - isteğe özel durumun tamamı
/// <see cref="IDbTargetContext"/>'in <see cref="AsyncLocal{T}"/> deposunda yaşar - singleton yaşam
/// süresini doğru kılan şeydir.
/// </para>
/// <para>
/// <strong>Known limitation.</strong> A connection string can only be assigned while the connection
/// is closed. EF Core opens late and closes early, so in the common case every operation gets a fresh
/// routing decision. It does <em>not</em> close the connection between operations while an explicit
/// transaction is active or after an explicit <c>Database.OpenConnection()</c>: there the route is
/// fixed at the moment the connection was opened. Start such work inside an
/// <see cref="IDbTargetContext.UseTarget"/> scope.
/// </para>
/// <para>
/// TR: <strong>Bilinen sınır.</strong> Bağlantı dizesi yalnızca bağlantı kapalıyken atanabilir. EF
/// Core geç açıp erken kapattığı için olağan durumda her işlem yeni bir yönlendirme kararı alır.
/// Ancak açık bir transaction etkinken veya açık bir <c>Database.OpenConnection()</c> çağrısından
/// sonra bağlantıyı işlemler arasında kapatmaz; bu durumda rota, bağlantının açıldığı anda
/// sabitlenir. Bu tür işleri bir <see cref="IDbTargetContext.UseTarget"/> scope'u içinde başlatın.
/// </para>
/// </remarks>
public sealed class MasterReplicaDbInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// Above this many replicas the health filter's bitmask no longer fits, and every replica is
    /// simply tried once in selector order.
    /// </summary>
    private const int MaxTrackedReplicas = 64;

    private readonly IReplicaHealthMonitor _health;
    private readonly ILogger<MasterReplicaDbInterceptor> _logger;
    private readonly MasterReplicaOptions _options;
    private readonly IDbConnectionStringResolver _resolver;
    private readonly ConnectionRouteRegistry _routes;
    private readonly IReplicaSelector _selector;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the interceptor.
    /// <para>TR: Interceptor'ı oluşturur.</para>
    /// </summary>
    /// <param name="targetContext">
    /// Ambient store holding the target for the current flow.
    /// <para>TR: Geçerli akışın hedefini tutan ortam deposu.</para>
    /// </param>
    /// <param name="resolver">
    /// Supplies the master and replica connection strings.
    /// <para>TR: Master ve replica bağlantı dizelerini sağlar.</para>
    /// </param>
    /// <param name="selector">
    /// Chooses which replica to try first.
    /// <para>TR: İlk denenecek replica'yı seçer.</para>
    /// </param>
    /// <param name="health">
    /// Tracks which replicas recently refused a connection.
    /// <para>TR: Hangi replica'ların yakın zamanda bağlantıyı reddettiğini izler.</para>
    /// </param>
    /// <param name="routes">
    /// Records which replica each connection was routed to, so a failure that only surfaces while a
    /// command runs can still be attributed to the right node.
    /// <para>
    /// TR: Her bağlantının hangi replica'ya yönlendirildiğini kaydeder; böylece yalnızca komut
    /// çalışırken yüzeye çıkan bir hata da doğru düğüme atfedilebilir.
    /// </para>
    /// </param>
    /// <param name="options">
    /// The configured master/replica options.
    /// <para>TR: Yapılandırılmış master/replica ayarları.</para>
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink. Connection strings are never written to it.
    /// <para>TR: Tanılama hedefi. Bağlantı dizeleri buraya asla yazılmaz.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    public MasterReplicaDbInterceptor(
        IDbTargetContext targetContext,
        IDbConnectionStringResolver resolver,
        IReplicaSelector selector,
        IReplicaHealthMonitor health,
        ConnectionRouteRegistry routes,
        IOptions<MasterReplicaOptions> options,
        ILogger<MasterReplicaDbInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _targetContext = targetContext;
        _resolver = resolver;
        _selector = selector;
        _health = health;
        _routes = routes;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventData);

        if (!CanRoute(connection, eventData))
        {
            return result;
        }

        if (ResolveTarget(eventData) == DbTarget.Master)
        {
            AssignMaster(connection, eventData);
            return result;
        }

        var replicas = _resolver.GetReplicaConnectionStrings();
        if (replicas.Count == 0)
        {
            return FallBackToMaster(connection, eventData, result, replicaCount: 0, failures: null);
        }

        List<Exception>? failures = null;

        foreach (var index in AttemptOrder(replicas.Count))
        {
            try
            {
                connection.ConnectionString = replicas[index];
                connection.Open();
                _health.ReportSuccess(index);
                _routes.RecordReplica(connection, index);
                Log.OpenedReplica(_logger, eventData.ConnectionId, index);

                // EF Core must not open it a second time.
                return InterceptionResult.Suppress();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RecordReplicaFailure(connection, eventData, index, exception, ref failures);
            }
        }

        return FallBackToMaster(connection, eventData, result, replicas.Count, failures);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventData);

        if (!CanRoute(connection, eventData))
        {
            return result;
        }

        if (ResolveTarget(eventData) == DbTarget.Master)
        {
            AssignMaster(connection, eventData);
            return result;
        }

        var replicas = _resolver.GetReplicaConnectionStrings();
        if (replicas.Count == 0)
        {
            return FallBackToMaster(connection, eventData, result, replicaCount: 0, failures: null);
        }

        List<Exception>? failures = null;

        foreach (var index in AttemptOrder(replicas.Count))
        {
            try
            {
                connection.ConnectionString = replicas[index];
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                _health.ReportSuccess(index);
                _routes.RecordReplica(connection, index);
                Log.OpenedReplica(_logger, eventData.ConnectionId, index);

                return InterceptionResult.Suppress();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RecordReplicaFailure(connection, eventData, index, exception, ref failures);
            }
        }

        return FallBackToMaster(connection, eventData, result, replicas.Count, failures);
    }

    /// <summary>
    /// Yields replica indices in the order they should be dialled: the ones believed to be reachable
    /// first, in selector order, then the ones in their failure cooldown as a last resort.
    /// <para>
    /// TR: Replica indekslerini bağlanılacakları sırayla üretir: önce erişilebilir kabul edilenler
    /// seçicinin belirlediği sırayla, ardından son çare olarak hata bekleme süresindekiler.
    /// </para>
    /// </summary>
    /// <param name="replicaCount">
    /// How many replicas are configured.
    /// <para>TR: Tanımlı replica sayısı.</para>
    /// </param>
    /// <returns>
    /// Each replica index exactly once.
    /// <para>TR: Her replica indeksi tam olarak bir kez.</para>
    /// </returns>
    private IEnumerable<int> AttemptOrder(int replicaCount)
    {
        var start = _selector.SelectStartIndex(replicaCount);
        if ((uint)start >= (uint)replicaCount)
        {
            start = 0;
        }

        if (replicaCount > MaxTrackedReplicas)
        {
            for (var offset = 0; offset < replicaCount; offset++)
            {
                yield return (start + offset) % replicaCount;
            }

            yield break;
        }

        // Pass one: replicas the health monitor believes are up. Anything skipped is remembered in a
        // bitmask so pass two can retry exactly those, and never one that pass one already tried and
        // failed - which is what stops a failed replica from being dialled twice per operation.
        ulong deferred = 0;

        for (var offset = 0; offset < replicaCount; offset++)
        {
            var index = (start + offset) % replicaCount;
            if (_health.IsAvailable(index))
            {
                yield return index;
            }
            else
            {
                deferred |= 1UL << index;
            }
        }

        if (deferred == 0)
        {
            yield break;
        }

        for (var offset = 0; offset < replicaCount; offset++)
        {
            var index = (start + offset) % replicaCount;
            if ((deferred & (1UL << index)) != 0)
            {
                yield return index;
            }
        }
    }

    private void RecordReplicaFailure(
        DbConnection connection,
        ConnectionEventData eventData,
        int index,
        Exception exception,
        ref List<Exception>? failures)
    {
        _health.ReportFailure(index, exception);
        Log.ReplicaOpenFailed(_logger, index, eventData.ConnectionId, exception);

        (failures ??= new List<Exception>()).Add(exception);

        // A failed open normally leaves the connection closed, but a provider that got further than
        // that would refuse the next connection-string assignment.
        CloseQuietly(connection);
    }

    private InterceptionResult FallBackToMaster(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        int replicaCount,
        List<Exception>? failures)
    {
        if (!_options.AllowReplicaFallbackToMaster)
        {
            throw failures is { Count: > 0 }
                ? ReplicaUnavailableException.ForFailedAttempts(replicaCount, failures)
                : ReplicaUnavailableException.ForMissingConfiguration();
        }

        if (replicaCount == 0)
        {
            Log.NoReplicaConfigured(_logger, eventData.ConnectionId);
        }
        else
        {
            Log.FallingBackToMaster(_logger, replicaCount, eventData.ConnectionId);
        }

        AssignMaster(connection, eventData);

        // Not suppressed: EF Core opens the master connection itself, so its execution strategy and
        // its own error handling still apply.
        return result;
    }

    private void AssignMaster(DbConnection connection, ConnectionEventData eventData)
    {
        var connectionString = _resolver.GetMasterConnectionString();

        if (!string.Equals(connection.ConnectionString, connectionString, StringComparison.Ordinal))
        {
            connection.ConnectionString = connectionString;
        }

        _routes.RecordMaster(connection);
        Log.RoutedToMaster(_logger, eventData.ConnectionId);
    }

    private bool CanRoute(DbConnection connection, ConnectionEventData eventData)
    {
        if (connection.State == ConnectionState.Closed)
        {
            return true;
        }

        Log.ConnectionAlreadyOpen(_logger, eventData.ConnectionId, connection.State.ToString());
        return false;
    }

    private DbTarget ResolveTarget(ConnectionEventData eventData)
    {
        var target = _targetContext.CurrentTarget;

        if (target == DbTarget.Master || !_options.ForceMasterInsideTransaction)
        {
            return target;
        }

        if (eventData.Context?.Database.CurrentTransaction is not null || Transaction.Current is not null)
        {
            Log.TransactionForcedMaster(_logger, eventData.ConnectionId);
            return DbTarget.Master;
        }

        return target;
    }

    private static void CloseQuietly(DbConnection connection)
    {
        if (connection.State == ConnectionState.Closed)
        {
            return;
        }

        try
        {
            connection.Close();
        }
        catch (Exception)
        {
            // The connection is already unusable and the original open failure is the one that
            // matters; swallowing this keeps it as the reported cause.
        }
    }
}
