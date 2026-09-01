using System;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Remembers which replica each live <see cref="DbConnection"/> was routed to, so a failure that
/// only surfaces later - while a command runs - can still be attributed to the right node.
/// <para>
/// TR: Her canlı <see cref="DbConnection"/> nesnesinin hangi replica'ya yönlendirildiğini hatırlar;
/// böylece yalnızca sonradan - bir komut çalışırken - yüzeye çıkan bir hata yine de doğru düğüme
/// atfedilebilir.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// This exists because opening a connection is not the same as reaching a server. ADO.NET pools
/// connections: when a replica dies while the pool still holds warm handles to it,
/// <c>OpenAsync</c> hands one back without a network round trip and reports success. The socket is
/// dead, but nothing discovers that until the first command runs - long after the routing decision
/// was made and far too late to choose a different replica for that attempt.
/// </para>
/// <para>
/// TR: Bu tip, bir bağlantıyı açmanın sunucuya erişmekle aynı şey olmamasından doğdu. ADO.NET
/// bağlantıları havuzlar: bir replica, havuz hâlâ ona ait sıcak tutamaçlar tutarken çökerse,
/// <c>OpenAsync</c> ağ turu yapmadan bunlardan birini geri verir ve başarı bildirir. Soket ölüdür
/// ama bunu ilk komut çalışana kadar kimse fark etmez - yönlendirme kararından çok sonra ve o
/// deneme için başka bir replica seçmek için fazlasıyla geç.
/// </para>
/// <para>
/// Recording the route lets <see cref="ReplicaCommandFailureInterceptor"/> mark that replica down
/// the moment the command fails, so the very next request avoids it instead of drawing another dead
/// handle from the same pool.
/// </para>
/// <para>
/// TR: Rotayı kaydetmek, <see cref="ReplicaCommandFailureInterceptor"/>'ın komut hata verir vermez o
/// replica'yı düşmüş olarak işaretlemesini sağlar; böylece hemen sonraki istek aynı havuzdan bir ölü
/// tutamaç daha çekmek yerine o düğümden kaçınır.
/// </para>
/// <para>
/// Entries are held weakly and disappear with the connection, so nothing needs to be cleaned up.
/// </para>
/// <para>
/// TR: Kayıtlar zayıf referansla tutulur ve bağlantıyla birlikte yok olur; temizlenmesi gereken bir
/// şey kalmaz.
/// </para>
/// </remarks>
public sealed class ConnectionRouteRegistry
{
    /// <summary>Marks a connection that was routed to the master rather than to a replica.</summary>
    private const int MasterRoute = -1;

    private readonly ConditionalWeakTable<DbConnection, StrongBox<int>> _routes = new();

    /// <summary>
    /// Records that <paramref name="connection"/> was routed to the replica at
    /// <paramref name="replicaIndex"/>.
    /// <para>
    /// TR: <paramref name="connection"/> bağlantısının <paramref name="replicaIndex"/> indeksli
    /// replica'ya yönlendirildiğini kaydeder.
    /// </para>
    /// </summary>
    /// <param name="connection">
    /// The connection that was just routed.
    /// <para>TR: Az önce yönlendirilen bağlantı.</para>
    /// </param>
    /// <param name="replicaIndex">
    /// Index of the replica it was pointed at.
    /// <para>TR: Yönlendirildiği replica'nın indeksi.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="connection"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="connection"/> <see langword="null"/>.</para>
    /// </exception>
    public void RecordReplica(DbConnection connection, int replicaIndex)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _routes.AddOrUpdate(connection, new StrongBox<int>(replicaIndex));
    }

    /// <summary>
    /// Records that <paramref name="connection"/> was routed to the master, clearing any earlier
    /// replica attribution.
    /// <para>
    /// TR: <paramref name="connection"/> bağlantısının master'a yönlendirildiğini kaydeder ve varsa
    /// önceki replica atfını temizler.
    /// </para>
    /// </summary>
    /// <param name="connection">
    /// The connection that was just routed.
    /// <para>TR: Az önce yönlendirilen bağlantı.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="connection"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="connection"/> <see langword="null"/>.</para>
    /// </exception>
    public void RecordMaster(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _routes.AddOrUpdate(connection, new StrongBox<int>(MasterRoute));
    }

    /// <summary>
    /// Reports which replica a connection was routed to, if it was routed to one at all.
    /// <para>
    /// TR: Bir bağlantının hangi replica'ya yönlendirildiğini - yönlendirildiyse - bildirir.
    /// </para>
    /// </summary>
    /// <param name="connection">
    /// The connection to look up.
    /// <para>TR: Sorgulanacak bağlantı.</para>
    /// </param>
    /// <param name="replicaIndex">
    /// Receives the replica index when the method returns <see langword="true"/>.
    /// <para>
    /// TR: Metot <see langword="true"/> döndürdüğünde replica indeksini alır.
    /// </para>
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the connection was routed to a replica; <see langword="false"/>
    /// when it went to the master or was never routed by this package.
    /// <para>
    /// TR: Bağlantı bir replica'ya yönlendirildiyse <see langword="true"/>; master'a gittiyse veya bu
    /// paket tarafından hiç yönlendirilmediyse <see langword="false"/>.
    /// </para>
    /// </returns>
    public bool TryGetReplicaIndex(DbConnection? connection, out int replicaIndex)
    {
        replicaIndex = MasterRoute;

        if (connection is null || !_routes.TryGetValue(connection, out var route) || route.Value == MasterRoute)
        {
            return false;
        }

        replicaIndex = route.Value;
        return true;
    }
}
