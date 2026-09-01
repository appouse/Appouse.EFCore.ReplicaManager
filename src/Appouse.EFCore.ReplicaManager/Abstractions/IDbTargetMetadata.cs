namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Marks a piece of endpoint metadata (typically an attribute) that pins an MVC action,
/// a Razor Page handler or a Minimal API endpoint to a specific <see cref="DbTarget"/>.
/// </summary>
/// <remarks>
/// Both <see cref="UseWriteDbAttribute"/> and <see cref="UseReadDbAttribute"/> implement this
/// interface, which lets the routing components resolve either of them with a single
/// <c>GetMetadata&lt;IDbTargetMetadata&gt;()</c> lookup. Because
/// <see cref="Microsoft.AspNetCore.Http.EndpointMetadataCollection.GetMetadata{T}"/> returns the
/// <em>last</em> matching item and ASP.NET Core appends action-level metadata after
/// controller-level metadata, an attribute on the action automatically wins over an attribute on
/// the controller.
/// </remarks>
public interface IDbTargetMetadata
{
    /// <summary>
    /// Gets the database target the decorated endpoint must run against.
    /// </summary>
    DbTarget Target { get; }
}
