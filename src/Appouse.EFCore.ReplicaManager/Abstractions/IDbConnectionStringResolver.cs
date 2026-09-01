namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Translates a <see cref="DbTarget"/> into the connection string that
/// <see cref="ReadWriteDbInterceptor"/> assigns to the connection before opening it.
/// </summary>
/// <remarks>
/// Replace the default registration to source connection strings from somewhere other than
/// <see cref="ReadWriteOptions"/> - a multi-tenant catalogue, a secret store, or a service
/// discovery lookup:
/// <code>
/// services.AddEfCoreReadWriteSplit(o => { /* ... */ });
/// services.Replace(ServiceDescriptor.Singleton&lt;IDbConnectionStringResolver, TenantConnectionStringResolver&gt;());
/// </code>
/// Implementations must be thread-safe and are resolved as singletons.
/// </remarks>
public interface IDbConnectionStringResolver
{
    /// <summary>
    /// Returns the connection string to use for <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The database being targeted.</param>
    /// <returns>A non-empty connection string.</returns>
    string Resolve(DbTarget target);
}
