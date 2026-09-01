namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Decides which replica a connection should try <em>first</em> when more than one is configured.
/// <para>
/// TR: Birden fazla replica tanımlıysa, bir bağlantının <em>ilk olarak</em> hangi replica'yı
/// deneyeceğine karar verir.
/// </para>
/// </summary>
/// <remarks>
/// The selector only chooses a starting point. If that replica is unreachable,
/// <see cref="MasterReplicaDbInterceptor"/> walks the remaining replicas itself, so a selector never
/// has to implement retry or failover.
/// <para>
/// TR: Seçici yalnızca bir başlangıç noktası belirler. O replica'ya erişilemezse
/// <see cref="MasterReplicaDbInterceptor"/> kalan replica'ları kendisi dolaşır; dolayısıyla bir
/// seçicinin yeniden deneme veya failover uygulaması gerekmez.
/// </para>
/// <para>
/// The default implementation is <see cref="RoundRobinReplicaSelector"/>. Replace it for weighted,
/// latency-aware or locality-aware selection. Implementations must be thread-safe and are resolved
/// as singletons.
/// </para>
/// <para>
/// TR: Varsayılan uygulama <see cref="RoundRobinReplicaSelector"/>'dır. Ağırlıklı, gecikme veya
/// konum duyarlı seçim için değiştirin. Uygulamalar thread-safe olmalıdır ve singleton olarak
/// çözümlenir.
/// </para>
/// </remarks>
public interface IReplicaSelector
{
    /// <summary>
    /// Returns the index of the replica to try first.
    /// <para>TR: İlk denenecek replica'nın indeksini döndürür.</para>
    /// </summary>
    /// <param name="replicaCount">
    /// How many replicas are configured. Always one or more.
    /// <para>TR: Tanımlı replica sayısı. Her zaman bir veya daha fazladır.</para>
    /// </param>
    /// <returns>
    /// An index in the range <c>[0, replicaCount)</c>. A value outside that range is clamped by the
    /// caller rather than treated as an error.
    /// <para>
    /// TR: <c>[0, replicaCount)</c> aralığında bir indeks. Aralık dışındaki bir değer hata sayılmaz,
    /// çağıran tarafından aralığa çekilir.
    /// </para>
    /// </returns>
    int SelectStartIndex(int replicaCount);
}
