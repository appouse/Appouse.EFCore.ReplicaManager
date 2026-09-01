using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Routes the decorated MVC controller, action, Razor Page or Minimal API endpoint to a read
/// replica, overriding the HTTP verb convention.
/// </summary>
/// <remarks>
/// Use it for a non-<c>GET</c> endpoint that only reads - a <c>POST</c> carrying a large search
/// payload, a report generation call, a batch lookup - so the master is not burdened with traffic
/// it does not need to serve. An attribute on the action wins over one on the controller.
/// </remarks>
/// <example>
/// Controller:
/// <code>
/// [HttpPost("search")]
/// [UseReadDb] // a POST, but read-only: the filter payload is simply too big for a query string
/// public Task&lt;IReadOnlyList&lt;OrderDto&gt;&gt; Search(SearchRequest request) =&gt; _orders.SearchAsync(request);
/// </code>
/// Minimal API:
/// <code>
/// app.MapPost("/orders/search", handler).UseReadDb();
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = true)]
public sealed class UseReadDbAttribute : Attribute, IDbTargetMetadata
{
    /// <inheritdoc />
    public DbTarget Target => DbTarget.ReadReplica;
}
