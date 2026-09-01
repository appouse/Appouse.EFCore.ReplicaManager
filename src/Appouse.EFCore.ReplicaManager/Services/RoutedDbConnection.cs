using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// A borrowed, already-routed <see cref="DbConnection"/> that returns itself to the
/// <see cref="DbContext"/> when disposed. Hand <see cref="Connection"/> to Dapper, or to any raw
/// ADO.NET code, and let the <c>using</c> take care of closing it.
/// <para>
/// TR: Dispose edildiğinde kendisini <see cref="DbContext"/> nesnesine geri veren, ödünç alınmış ve
/// halihazırda yönlendirilmiş bir <see cref="DbConnection"/>. <see cref="Connection"/> özelliğini
/// Dapper'a veya herhangi bir ham ADO.NET koduna verin; kapatma işini <c>using</c> üstlensin.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Disposal calls <c>CloseConnection</c> on the context rather than <c>Close</c> on the connection,
/// because EF Core reference-counts the connection: closing it directly would pull it out from under
/// a query the context still believes is running.
/// </para>
/// <para>
/// TR: Dispose işlemi, bağlantı üzerinde <c>Close</c> değil context üzerinde <c>CloseConnection</c>
/// çağırır; çünkü EF Core bağlantıyı referans sayarak yönetir ve doğrudan kapatmak, context'in hâlâ
/// çalıştığını düşündüğü bir sorgunun altından bağlantıyı çekmek olurdu.
/// </para>
/// <para>
/// Disposing more than once is a no-op.
/// </para>
/// <para>TR: Birden fazla kez dispose edilmesi bir şey değiştirmez.</para>
/// </remarks>
public sealed class RoutedDbConnection : IAsyncDisposable, IDisposable
{
    private DatabaseFacade? _database;

    internal RoutedDbConnection(DatabaseFacade database)
    {
        _database = database;
        Connection = database.GetDbConnection();
    }

    /// <summary>
    /// The open connection, already pointing at the master or at a replica according to the ambient
    /// target.
    /// <para>
    /// TR: Ortam hedefine göre master'a veya bir replica'ya işaret eden, açık durumdaki bağlantı.
    /// </para>
    /// </summary>
    public DbConnection Connection { get; }

    /// <summary>
    /// Returns the connection to the <see cref="DbContext"/>.
    /// <para>TR: Bağlantıyı <see cref="DbContext"/> nesnesine geri verir.</para>
    /// </summary>
    public void Dispose() => Interlocked.Exchange(ref _database, null)?.CloseConnection();

    /// <summary>
    /// Returns the connection to the <see cref="DbContext"/>.
    /// <para>TR: Bağlantıyı <see cref="DbContext"/> nesnesine geri verir.</para>
    /// </summary>
    /// <returns>
    /// A task that completes once the connection has been returned.
    /// <para>TR: Bağlantı geri verildiğinde tamamlanan görev.</para>
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        var database = Interlocked.Exchange(ref _database, null);
        if (database is not null)
        {
            await database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
