using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Routes the decorated MVC controller, action, Razor Page or Minimal API endpoint to the master
/// database, overriding the HTTP verb convention.
/// <para>
/// TR: İşaretlenen MVC controller'ını, action'ını, Razor Page'ini veya Minimal API endpoint'ini HTTP
/// metodu konvansiyonunu geçersiz kılarak master veritabanına yönlendirir.
/// </para>
/// </summary>
/// <remarks>
/// Use it for a <c>GET</c> that must not observe replication lag - a balance check straight after a
/// payment, a redirect that reads a row it just inserted, an admin screen that has to be exact. An
/// attribute on the action wins over one on the controller.
/// <para>
/// TR: Replikasyon gecikmesini görmemesi gereken <c>GET</c>'ler için kullanın: ödemenin hemen
/// ardından bakiye sorgusu, az önce eklediği satırı okuyan bir yönlendirme, kesin veri gerektiren bir
/// yönetim ekranı. Action üzerindeki attribute, controller üzerindekini yener.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpGet("{id:guid}/balance")]
/// [UseMasterDb] // must not read a stale balance
/// public Task&lt;decimal&gt; GetBalance(Guid id) =&gt; _accounts.GetBalanceAsync(id);
///
/// app.MapGet("/accounts/{id:guid}/balance", handler).UseMasterDb();
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = true)]
public sealed class UseMasterDbAttribute : Attribute, IDbTargetMetadata
{
    /// <inheritdoc />
    public DbTarget Target => DbTarget.Master;
}
