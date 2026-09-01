using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Routes the decorated MVC controller, action, Razor Page or Minimal API endpoint to the
/// primary (master) database, overriding the HTTP verb convention.
/// </summary>
/// <remarks>
/// Use it for a <c>GET</c> that must not observe replication lag - a balance check straight after
/// a payment, a redirect that reads a row it just inserted, an admin screen that has to be exact.
/// An attribute on the action wins over one on the controller.
/// </remarks>
/// <example>
/// Controller:
/// <code>
/// [HttpGet("{id:guid}/balance")]
/// [UseWriteDb] // must not read a stale balance
/// public Task&lt;decimal&gt; GetBalance(Guid id) =&gt; _accounts.GetBalanceAsync(id);
/// </code>
/// Minimal API:
/// <code>
/// app.MapGet("/accounts/{id:guid}/balance", handler).UseWriteDb();
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = true)]
public sealed class UseWriteDbAttribute : Attribute, IDbTargetMetadata
{
    /// <inheritdoc />
    public DbTarget Target => DbTarget.WriteMaster;
}
