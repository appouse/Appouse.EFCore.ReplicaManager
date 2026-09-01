using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// What a single replica answered when it was probed.
/// <para>TR: Bir replica sondalandığında verdiği yanıt.</para>
/// </summary>
public sealed class ReplicaProbeResult
{
    internal ReplicaProbeResult(int replicaIndex, bool isReachable, TimeSpan duration, string? error)
    {
        ReplicaIndex = replicaIndex;
        IsReachable = isReachable;
        Duration = duration;
        Error = error;
    }

    /// <summary>
    /// The replica's position in the configured list, which is also how
    /// <see cref="IReplicaHealthMonitor"/> identifies it.
    /// <para>
    /// TR: Replica'nın yapılandırılmış listedeki sırası;
    /// <see cref="IReplicaHealthMonitor"/> onu bu indeksle tanır.
    /// </para>
    /// </summary>
    public int ReplicaIndex { get; }

    /// <summary>
    /// Whether the replica both accepted a connection and answered the validation query.
    /// <para>
    /// TR: Replica'nın hem bağlantıyı kabul edip hem doğrulama sorgusunu yanıtlayıp yanıtlamadığı.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Both halves matter. A pooled handle to a dead server accepts the connection and fails the
    /// query, which is exactly the case a probe exists to catch.
    /// <para>
    /// TR: İki yarısı da önemlidir. Ölü bir sunucuya ait havuzlanmış bir tutamaç bağlantıyı kabul
    /// eder ama sorguda hata verir; sondanın var olma sebebi tam olarak bu durumu yakalamaktır.
    /// </para>
    /// </remarks>
    public bool IsReachable { get; }

    /// <summary>
    /// How long the probe took, connection and query together. Useful as a rough latency signal.
    /// <para>
    /// TR: Sondanın ne kadar sürdüğü; bağlantı ve sorgu birlikte. Kaba bir gecikme göstergesi olarak
    /// kullanışlıdır.
    /// </para>
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// The provider's message when the probe failed, or <see langword="null"/> when it succeeded.
    /// <para>
    /// TR: Sonda başarısız olduysa sağlayıcının mesajı; başarılıysa <see langword="null"/>.
    /// </para>
    /// </summary>
    public string? Error { get; }
}
