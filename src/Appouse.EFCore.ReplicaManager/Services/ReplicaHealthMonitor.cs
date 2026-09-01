using System;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Default <see cref="IReplicaHealthMonitor"/>: after a replica refuses a connection it is skipped
/// for <see cref="MasterReplicaOptions.ReplicaFailureCooldown"/>, then tried again.
/// <para>
/// TR: Varsayılan <see cref="IReplicaHealthMonitor"/> uygulaması: bir replica bağlantıyı
/// reddettikten sonra <see cref="MasterReplicaOptions.ReplicaFailureCooldown"/> süresince atlanır,
/// ardından yeniden denenir.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Without a cooldown, a replica that is down would be dialled on every single request and each
/// request would pay its full connection timeout before failing over. The cooldown turns that into
/// one timeout per interval.
/// </para>
/// <para>
/// TR: Bekleme süresi olmasaydı, çökmüş bir replica her istekte yeniden aranır ve her istek failover
/// öncesinde tam bağlantı zaman aşımını öderdi. Bekleme süresi bunu aralık başına tek bir zaman
/// aşımına indirir.
/// </para>
/// <para>
/// State is a single <see cref="long"/> per replica holding the tick count at which the replica may
/// be retried, read and written with <see cref="Volatile"/> accessors. There is no lock, and a
/// benign race merely costs one extra connection attempt.
/// <see cref="Environment.TickCount64"/> is used rather than the wall clock, so the cooldown is
/// immune to clock adjustments.
/// </para>
/// <para>
/// TR: Durum, replica başına tek bir <see cref="long"/> değerdir ve replica'nın yeniden
/// denenebileceği tick sayısını tutar; <see cref="Volatile"/> erişimcileriyle okunup yazılır. Kilit
/// yoktur; zararsız bir yarış durumu yalnızca fazladan bir bağlantı denemesine mal olur. Duvar saati
/// yerine <see cref="Environment.TickCount64"/> kullanılır; böylece bekleme süresi saat
/// değişikliklerinden etkilenmez.
/// </para>
/// </remarks>
public sealed class ReplicaHealthMonitor : IReplicaHealthMonitor
{
    private readonly long _cooldownMilliseconds;

    /// <summary>
    /// Per replica, the <see cref="Environment.TickCount64"/> value at which it may be tried again.
    /// Zero means healthy.
    /// </summary>
    private readonly long[] _retryAfter;

    /// <summary>
    /// Creates a monitor sized for the configured topology.
    /// <para>TR: Yapılandırılmış topolojiye göre boyutlandırılmış bir izleyici oluşturur.</para>
    /// </summary>
    /// <param name="resolver">
    /// Supplies the replica list, whose length fixes the number of tracked slots.
    /// <para>TR: İzlenecek yuva sayısını belirleyen replica listesini sağlar.</para>
    /// </param>
    /// <param name="options">
    /// The configured master/replica options.
    /// <para>TR: Yapılandırılmış master/replica ayarları.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    public ReplicaHealthMonitor(IDbConnectionStringResolver resolver, IOptions<MasterReplicaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);

        _retryAfter = new long[resolver.GetReplicaConnectionStrings().Count];

        var cooldown = options.Value.ReplicaFailureCooldown;
        _cooldownMilliseconds = cooldown <= TimeSpan.Zero ? 0 : (long)cooldown.TotalMilliseconds;
    }

    /// <inheritdoc />
    public bool IsAvailable(int replicaIndex)
    {
        if ((uint)replicaIndex >= (uint)_retryAfter.Length)
        {
            return true;
        }

        var retryAfter = Volatile.Read(ref _retryAfter[replicaIndex]);
        return retryAfter == 0 || Environment.TickCount64 >= retryAfter;
    }

    /// <inheritdoc />
    public void ReportSuccess(int replicaIndex)
    {
        if ((uint)replicaIndex < (uint)_retryAfter.Length)
        {
            Volatile.Write(ref _retryAfter[replicaIndex], 0);
        }
    }

    /// <inheritdoc />
    public void ReportFailure(int replicaIndex, Exception exception)
    {
        if ((uint)replicaIndex >= (uint)_retryAfter.Length)
        {
            return;
        }

        // A zero cooldown means "retry every replica on every connection": record nothing, so the
        // replica stays in the healthy set.
        Volatile.Write(
            ref _retryAfter[replicaIndex],
            _cooldownMilliseconds == 0 ? 0 : Environment.TickCount64 + _cooldownMilliseconds);
    }
}
