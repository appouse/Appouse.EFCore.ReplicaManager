using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Bridges master/replica routing to Dapper and to raw ADO.NET.
/// <para>TR: Master/replica yönlendirmesini Dapper'a ve ham ADO.NET'e bağlar.</para>
/// </summary>
/// <remarks>
/// <para>
/// The routing interceptor fires on the paths EF Core itself opens.
/// <c>Database.GetDbConnection()</c> hands you the raw connection, and opening it yourself - or
/// letting Dapper open it - is a plain ADO.NET call EF Core never sees, so nothing rewrites the
/// connection string. Worse, an unrouted connection is not neutral: it carries whatever connection
/// string was last written to it, so raw access after an EF query inherits that query's route even
/// under a different scope, and a write issued that way can reach a read-only replica.
/// </para>
/// <para>
/// TR: Yönlendirme interceptor'ı, EF Core'un kendi açtığı yollarda tetiklenir.
/// <c>Database.GetDbConnection()</c> size ham bağlantıyı verir; onu kendiniz açmanız - ya da
/// Dapper'ın açması - EF Core'un hiç görmediği düz bir ADO.NET çağrısıdır ve bağlantı dizesini
/// kimse yeniden yazmaz. Dahası, yönlendirilmemiş bir bağlantı nötr değildir: kendisine en son
/// yazılan dizeyi taşır. Bu yüzden bir EF sorgusundan sonraki ham erişim, farklı bir scope içinde
/// bile o sorgunun rotasını devralır ve bu yolla yapılan bir yazma salt okunur bir replica'ya
/// ulaşabilir.
/// </para>
/// <para>
/// These helpers close that gap by letting EF Core open the connection, which routes it, applies
/// replica failover and updates replica health exactly as an EF query would.
/// </para>
/// <para>
/// TR: Bu yardımcılar, bağlantıyı EF Core'a açtırarak bu boşluğu kapatır; böylece bağlantı
/// yönlendirilir, replica failover'ı uygulanır ve replica sağlığı bir EF sorgusundaki gibi
/// güncellenir.
/// </para>
/// </remarks>
public static class ReplicaManagerDatabaseFacadeExtensions
{
    /// <summary>
    /// Opens the context's connection through EF Core against the target already in effect, and
    /// lends it to you until the returned handle is disposed.
    /// <para>
    /// TR: Context'in bağlantısını, halihazırda geçerli olan hedefe karşı EF Core üzerinden açar ve
    /// döndürülen tutamaç dispose edilene kadar size ödünç verir.
    /// </para>
    /// </summary>
    /// <param name="database">
    /// The context's database facade.
    /// <para>TR: Context'in veritabanı arayüzü.</para>
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the open.
    /// <para>TR: Açma işlemini iptal eder.</para>
    /// </param>
    /// <returns>
    /// A handle whose <see cref="RoutedDbConnection.Connection"/> is open and routed.
    /// <para>
    /// TR: <see cref="RoutedDbConnection.Connection"/> özelliği açık ve yönlendirilmiş olan tutamaç.
    /// </para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="database"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="database"/> <see langword="null"/>.</para>
    /// </exception>
    /// <remarks>
    /// "The target already in effect" means an enclosing <see cref="IDbTargetContext.UseTarget"/>
    /// scope if there is one, and <see cref="MasterReplicaOptions.DefaultTarget"/> otherwise. Say
    /// nothing and you get your configured default.
    /// <para>
    /// TR: "Halihazırda geçerli olan hedef", varsa çevreleyen bir
    /// <see cref="IDbTargetContext.UseTarget"/> scope'u, yoksa
    /// <see cref="MasterReplicaOptions.DefaultTarget"/> demektir. Hiçbir şey söylemezseniz
    /// yapılandırdığınız varsayılanı alırsınız.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// await using var routed = await db.Database.OpenRoutedConnectionAsync(cancellationToken);
    /// var orders = await routed.Connection.QueryAsync&lt;Order&gt;("SELECT * FROM Orders");
    /// </code>
    /// </example>
    public static async Task<RoutedDbConnection> OpenRoutedConnectionAsync(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new RoutedDbConnection(database);
    }

    /// <summary>
    /// Opens the context's connection through EF Core against <paramref name="target"/> - the master,
    /// or whichever replica is currently healthy - and lends it to you until the returned handle is
    /// disposed.
    /// <para>
    /// TR: Context'in bağlantısını <paramref name="target"/> hedefine - master'a veya o an sağlıklı
    /// olan replica'ya - karşı EF Core üzerinden açar ve döndürülen tutamaç dispose edilene kadar
    /// size ödünç verir.
    /// </para>
    /// </summary>
    /// <param name="database">
    /// The context's database facade.
    /// <para>TR: Context'in veritabanı arayüzü.</para>
    /// </param>
    /// <param name="target">
    /// Which database to open against. <see cref="DbTarget.Replica"/> picks a healthy replica and
    /// fails over between them; <see cref="DbTarget.Master"/> opens the single master.
    /// <para>
    /// TR: Hangi veritabanına açılacağı. <see cref="DbTarget.Replica"/> sağlıklı bir replica seçer ve
    /// aralarında failover yapar; <see cref="DbTarget.Master"/> tek master'a açar.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the open.
    /// <para>TR: Açma işlemini iptal eder.</para>
    /// </param>
    /// <returns>
    /// A handle whose <see cref="RoutedDbConnection.Connection"/> is open against
    /// <paramref name="target"/>.
    /// <para>
    /// TR: <see cref="RoutedDbConnection.Connection"/> özelliği <paramref name="target"/> hedefine
    /// açılmış olan tutamaç.
    /// </para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="database"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="database"/> <see langword="null"/>.</para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="target"/> is not a defined <see cref="DbTarget"/> value.
    /// <para>TR: <paramref name="target"/> tanımlı bir <see cref="DbTarget"/> değeri değil.</para>
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This <c>DbContext</c> was registered without master/replica splitting, so there is no routing
    /// to apply.
    /// <para>
    /// TR: Bu <c>DbContext</c> master/replica ayrımı olmadan kaydedilmiş; uygulanacak bir yönlendirme
    /// yok.
    /// </para>
    /// </exception>
    /// <exception cref="ReplicaUnavailableException">
    /// <see cref="DbTarget.Replica"/> was requested, none accepted the connection, and falling back
    /// to the master is disabled.
    /// <para>
    /// TR: <see cref="DbTarget.Replica"/> istendi, hiçbiri bağlantıyı kabul etmedi ve master'a düşme
    /// kapalı.
    /// </para>
    /// </exception>
    /// <remarks>
    /// The request only binds while the connection is being opened. If the context is already
    /// holding an open connection, EF Core hands back the existing one and its route stands, because
    /// a connection string cannot be changed while the connection is open.
    /// <para>
    /// TR: İstek yalnızca bağlantı açılırken bağlayıcıdır. Context zaten açık bir bağlantı tutuyorsa
    /// EF Core mevcut olanı verir ve onun rotası geçerli kalır; çünkü bağlantı açıkken bağlantı
    /// dizesi değiştirilemez.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// await using var routed = await db.Database.OpenRoutedConnectionAsync(DbTarget.Replica, cancellationToken);
    /// var report = await routed.Connection.QueryAsync&lt;Row&gt;("SELECT ... /* heavy, lag-tolerant */");
    /// </code>
    /// </example>
    public static async Task<RoutedDbConnection> OpenRoutedConnectionAsync(
        this DatabaseFacade database,
        DbTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        using (RequireTargetContext(database).UseTarget(target))
        {
            await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        return new RoutedDbConnection(database);
    }

    /// <summary>
    /// Synchronous counterpart of
    /// <see cref="OpenRoutedConnectionAsync(DatabaseFacade,CancellationToken)"/>.
    /// <para>
    /// TR: <see cref="OpenRoutedConnectionAsync(DatabaseFacade,CancellationToken)"/> metodunun
    /// senkron karşılığı.
    /// </para>
    /// </summary>
    /// <param name="database">
    /// The context's database facade.
    /// <para>TR: Context'in veritabanı arayüzü.</para>
    /// </param>
    /// <returns>
    /// A handle whose <see cref="RoutedDbConnection.Connection"/> is open and routed.
    /// <para>
    /// TR: <see cref="RoutedDbConnection.Connection"/> özelliği açık ve yönlendirilmiş olan tutamaç.
    /// </para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="database"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="database"/> <see langword="null"/>.</para>
    /// </exception>
    public static RoutedDbConnection OpenRoutedConnection(this DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.OpenConnection();
        return new RoutedDbConnection(database);
    }

    /// <summary>
    /// Synchronous counterpart of
    /// <see cref="OpenRoutedConnectionAsync(DatabaseFacade,DbTarget,CancellationToken)"/>.
    /// <para>
    /// TR: <see cref="OpenRoutedConnectionAsync(DatabaseFacade,DbTarget,CancellationToken)"/>
    /// metodunun senkron karşılığı.
    /// </para>
    /// </summary>
    /// <param name="database">
    /// The context's database facade.
    /// <para>TR: Context'in veritabanı arayüzü.</para>
    /// </param>
    /// <param name="target">
    /// Which database to open against.
    /// <para>TR: Hangi veritabanına açılacağı.</para>
    /// </param>
    /// <returns>
    /// A handle whose <see cref="RoutedDbConnection.Connection"/> is open against
    /// <paramref name="target"/>.
    /// <para>
    /// TR: <see cref="RoutedDbConnection.Connection"/> özelliği <paramref name="target"/> hedefine
    /// açılmış olan tutamaç.
    /// </para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="database"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="database"/> <see langword="null"/>.</para>
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This <c>DbContext</c> was registered without master/replica splitting.
    /// <para>TR: Bu <c>DbContext</c> master/replica ayrımı olmadan kaydedilmiş.</para>
    /// </exception>
    public static RoutedDbConnection OpenRoutedConnection(this DatabaseFacade database, DbTarget target)
    {
        ArgumentNullException.ThrowIfNull(database);

        using (RequireTargetContext(database).UseTarget(target))
        {
            database.OpenConnection();
        }

        return new RoutedDbConnection(database);
    }

    /// <summary>
    /// Asks every configured replica whether it is really there, by opening a connection to it and
    /// running a one-row query.
    /// <para>
    /// TR: Yapılandırılmış her replica'ya gerçekten orada olup olmadığını sorar: bağlantı açar ve tek
    /// satırlık bir sorgu çalıştırır.
    /// </para>
    /// </summary>
    /// <param name="database">
    /// The context's database facade, used only to learn the provider.
    /// <para>TR: Yalnızca sağlayıcıyı öğrenmek için kullanılan context veritabanı arayüzü.</para>
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the probe.
    /// <para>TR: Sondayı iptal eder.</para>
    /// </param>
    /// <returns>
    /// One result per configured replica, in configuration order. Empty when no replica is
    /// configured.
    /// <para>
    /// TR: Yapılandırma sırasına göre her replica için bir sonuç. Hiç replica tanımlı değilse boş.
    /// </para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="database"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="database"/> <see langword="null"/>.</para>
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This <c>DbContext</c> was registered without master/replica splitting.
    /// <para>TR: Bu <c>DbContext</c> master/replica ayrımı olmadan kaydedilmiş.</para>
    /// </exception>
    /// <remarks>
    /// <para>
    /// Opening alone does not prove anything: ADO.NET pools connections, so a handle to a server that
    /// has since died is handed back without a network round trip. The query is what forces the round
    /// trip, and it is why <see cref="ReplicaProbeResult.IsReachable"/> means both halves succeeded.
    /// </para>
    /// <para>
    /// TR: Yalnızca açmak hiçbir şey kanıtlamaz: ADO.NET bağlantıları havuzlar, bu yüzden o sırada
    /// çökmüş bir sunucuya ait tutamaç ağ turu yapılmadan geri verilir. Turu zorlayan şey sorgudur;
    /// <see cref="ReplicaProbeResult.IsReachable"/> özelliğinin iki yarının da başarılı olduğu
    /// anlamına gelmesinin sebebi de budur.
    /// </para>
    /// <para>
    /// The probe uses its own connections and never touches the context's, so it is safe to call at
    /// any time. Its results feed <see cref="IReplicaHealthMonitor"/>: a replica that answers has its
    /// cooldown cleared, and one that does not is stood down, so a scheduled probe doubles as a way
    /// to keep routing away from a node that is down.
    /// </para>
    /// <para>
    /// TR: Sonda kendi bağlantılarını kullanır ve context'inkine hiç dokunmaz; bu yüzden her an
    /// çağrılabilir. Sonuçları <see cref="IReplicaHealthMonitor"/> bileşenini besler: yanıt veren bir
    /// replica'nın bekleme süresi temizlenir, vermeyen dinlendirilir. Böylece zamanlanmış bir sonda,
    /// yönlendirmeyi çökmüş bir düğümden uzak tutmanın da bir yolu olur.
    /// </para>
    /// <para>
    /// Replicas are probed concurrently, so one unreachable node does not delay the rest.
    /// </para>
    /// <para>
    /// TR: Replica'lar eşzamanlı sondalanır; böylece erişilemeyen bir düğüm diğerlerini geciktirmez.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// foreach (var result in await db.Database.ProbeReplicasAsync(cancellationToken))
    /// {
    ///     Console.WriteLine(result.IsReachable
    ///         ? $"replica #{result.ReplicaIndex}: up in {result.Duration.TotalMilliseconds:N0} ms"
    ///         : $"replica #{result.ReplicaIndex}: DOWN - {result.Error}");
    /// }
    /// </code>
    /// </example>
    public static async Task<IReadOnlyList<ReplicaProbeResult>> ProbeReplicasAsync(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        var interceptor = RequireInterceptor(database);
        var replicas = interceptor.Resolver.GetReplicaConnectionStrings();

        if (replicas.Count == 0)
        {
            return Array.Empty<ReplicaProbeResult>();
        }

        // Cloned from the context's own connection, so the probe stays provider-agnostic.
        var factory = DbProviderFactories.GetFactory(database.GetDbConnection())
            ?? throw new InvalidOperationException(
                "The database provider did not expose a DbProviderFactory, so replicas cannot be probed " +
                "with connections of their own. Query the replicas directly instead, or enable " +
                $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.ValidateReplicaConnections)} " +
                "so every replica connection is validated as it is opened.");
        var sql = ReplicaValidationQuery.For(interceptor.Options.ReplicaValidationQuery, database.ProviderName);
        var timeout = Math.Max(1, (int)interceptor.Options.ReplicaValidationTimeout.TotalSeconds);

        var probes = new Task<ReplicaProbeResult>[replicas.Count];
        for (var i = 0; i < replicas.Count; i++)
        {
            probes[i] = ProbeOneAsync(factory, replicas[i], i, sql, timeout, interceptor.Health, cancellationToken);
        }

        return await Task.WhenAll(probes).ConfigureAwait(false);
    }

    private static async Task<ReplicaProbeResult> ProbeOneAsync(
        DbProviderFactory factory,
        string connectionString,
        int replicaIndex,
        string sql,
        int timeoutSeconds,
        IReplicaHealthMonitor health,
        CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();

        try
        {
            await using var connection = factory.CreateConnection()
                ?? throw new InvalidOperationException("The provider factory returned no connection.");

            connection.ConnectionString = connectionString;
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = timeoutSeconds;
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            health.ReportSuccess(replicaIndex);
            return new ReplicaProbeResult(replicaIndex, isReachable: true, Stopwatch.GetElapsedTime(start), error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            health.ReportFailure(replicaIndex, exception);
            return new ReplicaProbeResult(replicaIndex, isReachable: false, Stopwatch.GetElapsedTime(start), exception.Message);
        }
    }

    /// <summary>
    /// Finds the ambient target store by locating this context's own routing interceptor, which also
    /// proves the context is actually wired for master/replica splitting.
    /// <para>
    /// TR: Ortam hedef deposunu, bu context'in kendi yönlendirme interceptor'ını bularak elde eder;
    /// bu aynı zamanda context'in gerçekten master/replica ayrımına bağlı olduğunu da kanıtlar.
    /// </para>
    /// </summary>
    /// <param name="database">
    /// The context's database facade.
    /// <para>TR: Context'in veritabanı arayüzü.</para>
    /// </param>
    /// <returns>
    /// The ambient target store.
    /// <para>TR: Ortam hedef deposu.</para>
    /// </returns>
    private static IDbTargetContext RequireTargetContext(DatabaseFacade database)
    {
        return RequireInterceptor(database).TargetContext;
    }

    private static MasterReplicaDbInterceptor RequireInterceptor(DatabaseFacade database)
    {
        var interceptor = database
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .Interceptors?
            .OfType<MasterReplicaDbInterceptor>()
            .FirstOrDefault();

        return interceptor
               ?? throw new InvalidOperationException(
                   "This DbContext was registered without master/replica splitting, so a target cannot be " +
                   "applied to it. Register it with services.AddMasterReplicaDbContext<TContext>(...), or, if " +
                   "you call AddDbContext yourself, add options.UseMasterReplicaSplitting(serviceProvider) " +
                   "inside it.");
    }
}
