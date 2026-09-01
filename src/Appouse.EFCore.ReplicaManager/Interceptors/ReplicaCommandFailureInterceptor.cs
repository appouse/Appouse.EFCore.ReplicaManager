using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Catches the replica failures that opening a connection cannot: a node that dies while ADO.NET
/// still holds warm pooled handles to it, whose first symptom is a failing command rather than a
/// failing open.
/// <para>
/// TR: Bağlantı açmanın yakalayamadığı replica hatalarını yakalar: ADO.NET hâlâ ona ait sıcak
/// havuzlanmış tutamaçlar tutarken çöken bir düğüm - ki ilk belirtisi açılışın değil, komutun hata
/// vermesidir.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// This interceptor does not retry the failed command; that is EF Core's execution strategy's job,
/// and <c>EnableRetryOnFailure</c> composes with this package exactly as you would hope - the retry
/// opens a fresh connection, which routes to a replica this interceptor has already marked down.
/// What it does is make sure the failure is not forgotten, so the request after it does not draw
/// another dead handle from the same pool.
/// </para>
/// <para>
/// TR: Bu interceptor başarısız komutu yeniden denemez; bu, EF Core'un yürütme stratejisinin işidir
/// ve <c>EnableRetryOnFailure</c> bu paketle tam beklendiği gibi birleşir - yeniden deneme yeni bir
/// bağlantı açar, o da bu interceptor'ın çoktan düşmüş olarak işaretlediği replica'dan kaçınarak
/// yönlendirilir. Bu sınıfın yaptığı, hatanın unutulmamasını sağlamaktır; böylece sonraki istek aynı
/// havuzdan bir ölü tutamaç daha çekmez.
/// </para>
/// </remarks>
public sealed class ReplicaCommandFailureInterceptor : DbCommandInterceptor
{
    private readonly IReplicaHealthMonitor _health;
    private readonly ILogger<ReplicaCommandFailureInterceptor> _logger;
    private readonly ConnectionRouteRegistry _routes;

    /// <summary>
    /// Creates the interceptor.
    /// <para>TR: Interceptor'ı oluşturur.</para>
    /// </summary>
    /// <param name="routes">
    /// Tells which replica the failing connection was routed to.
    /// <para>TR: Hata veren bağlantının hangi replica'ya yönlendirildiğini bildirir.</para>
    /// </param>
    /// <param name="health">
    /// Receives the failure report.
    /// <para>TR: Hata bildirimini alır.</para>
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink.
    /// <para>TR: Tanılama hedefi.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    public ReplicaCommandFailureInterceptor(
        ConnectionRouteRegistry routes,
        IReplicaHealthMonitor health,
        ILogger<ReplicaCommandFailureInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(logger);

        _routes = routes;
        _health = health;
        _logger = logger;
    }

    /// <inheritdoc />
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        ReportIfReplica(command, eventData);
        base.CommandFailed(command, eventData);
    }

    /// <inheritdoc />
    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ReportIfReplica(command, eventData);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    private void ReportIfReplica(DbCommand? command, CommandErrorEventData? eventData)
    {
        if (eventData?.Exception is null || !_routes.TryGetReplicaIndex(command?.Connection, out var replicaIndex))
        {
            return;
        }

        _health.ReportFailure(replicaIndex, eventData.Exception);
        Log.ReplicaCommandFailed(_logger, replicaIndex, eventData.Exception);
    }
}
