using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Routes the decorated MVC controller, action, Razor Page or Minimal API endpoint to a read replica,
/// overriding the HTTP verb convention.
/// <para>
/// TR: İşaretlenen MVC controller'ını, action'ını, Razor Page'ini veya Minimal API endpoint'ini HTTP
/// metodu konvansiyonunu geçersiz kılarak bir okuma replica'sına yönlendirir.
/// </para>
/// </summary>
/// <remarks>
/// Use it for a non-<c>GET</c> endpoint that only reads - a <c>POST</c> carrying a large search
/// payload, a report generation call, a batch lookup - so the master is not burdened with traffic it
/// does not need to serve. An attribute on the action wins over one on the controller.
/// <para>
/// TR: Yalnızca okuma yapan <c>GET</c> dışı endpoint'ler için kullanın: büyük bir arama gövdesi
/// taşıyan <c>POST</c>, rapor üretimi, toplu sorgulama. Böylece master, karşılaması gerekmeyen
/// trafikle yorulmaz. Action üzerindeki attribute, controller üzerindekini yener.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpPost("search")]
/// [UseReplicaDb] // a POST, but read-only: the filter payload is simply too big for a query string
/// public Task&lt;IReadOnlyList&lt;OrderDto&gt;&gt; Search(SearchRequest request) =&gt; _orders.SearchAsync(request);
///
/// app.MapPost("/orders/search", handler).UseReplicaDb();
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = true)]
public sealed class UseReplicaDbAttribute : Attribute, IDbTargetMetadata
{
    /// <inheritdoc />
    public DbTarget Target => DbTarget.Replica;
}
