namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Identifies the physical database an operation must be routed to.
/// <para>TR: Bir işlemin hangi fiziksel veritabanına yönlendirileceğini belirtir.</para>
/// </summary>
public enum DbTarget
{
    /// <summary>
    /// The single primary database. Every write, and every read that must observe the most recent
    /// write, is routed here. There is exactly one master in the topology.
    /// <para>
    /// TR: Tek olan birincil veritabanı. Tüm yazmalar ve en güncel veriyi görmesi zorunlu olan
    /// okumalar buraya yönlendirilir. Topolojide her zaman tek bir master bulunur.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This member is deliberately <c>0</c>, so <c>default(DbTarget)</c> resolves to the master.
    /// An unresolved target must never silently fall through to a replica: the master is the only
    /// node guaranteed to be both readable and writable.
    /// <para>
    /// TR: Bu üye bilinçli olarak <c>0</c> değerindedir; böylece <c>default(DbTarget)</c> master'a
    /// karşılık gelir. Çözümlenememiş bir hedef asla sessizce replica'ya düşmemelidir: hem okunabilir
    /// hem yazılabilir olduğu garanti edilen tek düğüm master'dır.
    /// </para>
    /// </remarks>
    Master = 0,

    /// <summary>
    /// A read-only replica. One or many may be configured; the package picks one per connection and
    /// fails over to the next when a replica is unreachable.
    /// <para>
    /// TR: Salt okunur replica. Bir veya birden fazla tanımlanabilir; paket her bağlantı için birini
    /// seçer ve bir replica'ya erişilemediğinde otomatik olarak sıradakine geçer.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Only safe for queries that tolerate replication lag.
    /// <para>TR: Yalnızca replikasyon gecikmesini kaldırabilen sorgular için güvenlidir.</para>
    /// </remarks>
    Replica = 1,
}
