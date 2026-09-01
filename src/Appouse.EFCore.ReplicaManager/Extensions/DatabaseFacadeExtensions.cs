using System;
using System.Threading;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager;
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
/// yazılan dizeyi taşır. Bu yüzden bir EF sorgusundan sonra yapılan ham erişim, farklı bir scope
/// içinde bile o sorgunun rotasını devralır ve bu yolla yapılan bir yazma salt okunur bir
/// replica'ya ulaşabilir.
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
    /// Opens the context's connection through EF Core - so it is routed - and lends it to you until
    /// the returned handle is disposed.
    /// <para>
    /// TR: Context'in bağlantısını EF Core üzerinden açar - böylece yönlendirilir - ve döndürülen
    /// tutamaç dispose edilene kadar size ödünç verir.
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
    /// A handle whose <see cref="RoutedDbConnection.Connection"/> is open and routed, and which
    /// returns the connection to the context when disposed.
    /// <para>
    /// TR: <see cref="RoutedDbConnection.Connection"/> özelliği açık ve yönlendirilmiş olan, dispose
    /// edildiğinde bağlantıyı context'e geri veren bir tutamaç.
    /// </para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="database"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="database"/> <see langword="null"/>.</para>
    /// </exception>
    /// <exception cref="ReplicaUnavailableException">
    /// A replica was requested, none accepted the connection, and falling back to the master is
    /// disabled.
    /// <para>
    /// TR: Replica istendi, hiçbiri bağlantıyı kabul etmedi ve master'a düşme kapalı.
    /// </para>
    /// </exception>
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
    /// <exception cref="ReplicaUnavailableException">
    /// A replica was requested, none accepted the connection, and falling back to the master is
    /// disabled.
    /// <para>
    /// TR: Replica istendi, hiçbiri bağlantıyı kabul etmedi ve master'a düşme kapalı.
    /// </para>
    /// </exception>
    /// <example>
    /// <code>
    /// using var routed = db.Database.OpenRoutedConnection();
    /// var orders = routed.Connection.Query&lt;Order&gt;("SELECT * FROM Orders");
    /// </code>
    /// </example>
    public static RoutedDbConnection OpenRoutedConnection(this DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.OpenConnection();
        return new RoutedDbConnection(database);
    }
}
