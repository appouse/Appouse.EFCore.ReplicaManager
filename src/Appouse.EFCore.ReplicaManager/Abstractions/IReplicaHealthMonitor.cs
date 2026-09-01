using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Remembers which replicas recently failed to accept a connection, so traffic is steered away from
/// a node that is down instead of paying its connection timeout on every request.
/// <para>
/// TR: Hangi replica'ların yakın zamanda bağlantı kabul etmediğini hatırlar; böylece çöken bir
/// düğümün bağlantı zaman aşımı her istekte yeniden ödenmek yerine trafik o düğümden uzaklaştırılır.
/// </para>
/// </summary>
/// <remarks>
/// Replicas are identified by their index in
/// <see cref="IDbConnectionStringResolver.GetReplicaConnectionStrings"/>. A replica marked
/// unavailable is not banned: it is merely moved to the back of the queue, and is tried again once
/// its cooldown expires or when every other replica has failed too.
/// <para>
/// TR: Replica'lar <see cref="IDbConnectionStringResolver.GetReplicaConnectionStrings"/> içindeki
/// indeksleriyle tanımlanır. Erişilemez olarak işaretlenen bir replica yasaklanmaz; yalnızca sıranın
/// sonuna alınır ve bekleme süresi dolduğunda ya da diğer tüm replica'lar da başarısız olduğunda
/// yeniden denenir.
/// </para>
/// <para>
/// Implementations must be thread-safe and are resolved as singletons.
/// </para>
/// <para>TR: Uygulamalar thread-safe olmalıdır ve singleton olarak çözümlenir.</para>
/// </remarks>
public interface IReplicaHealthMonitor
{
    /// <summary>
    /// Reports whether a replica is currently believed to be reachable.
    /// <para>TR: Bir replica'nın şu anda erişilebilir kabul edilip edilmediğini bildirir.</para>
    /// </summary>
    /// <param name="replicaIndex">
    /// Index of the replica.
    /// <para>TR: Replica'nın indeksi.</para>
    /// </param>
    /// <returns>
    /// <see langword="false"/> while the replica is inside its failure cooldown; otherwise
    /// <see langword="true"/>.
    /// <para>
    /// TR: Replica hata bekleme süresi içindeyse <see langword="false"/>, aksi hâlde
    /// <see langword="true"/>.
    /// </para>
    /// </returns>
    bool IsAvailable(int replicaIndex);

    /// <summary>
    /// Records that a connection to the replica opened successfully, clearing any cooldown.
    /// <para>
    /// TR: Replica'ya bağlantının başarıyla açıldığını kaydeder ve varsa bekleme süresini temizler.
    /// </para>
    /// </summary>
    /// <param name="replicaIndex">
    /// Index of the replica.
    /// <para>TR: Replica'nın indeksi.</para>
    /// </param>
    void ReportSuccess(int replicaIndex);

    /// <summary>
    /// Records that a connection to the replica could not be opened, starting its cooldown.
    /// <para>
    /// TR: Replica'ya bağlantının açılamadığını kaydeder ve bekleme süresini başlatır.
    /// </para>
    /// </summary>
    /// <param name="replicaIndex">
    /// Index of the replica.
    /// <para>TR: Replica'nın indeksi.</para>
    /// </param>
    /// <param name="exception">
    /// The failure reported by the provider.
    /// <para>TR: Sağlayıcının bildirdiği hata.</para>
    /// </param>
    void ReportFailure(int replicaIndex, Exception exception);
}
