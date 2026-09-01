using System.Collections.Generic;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Configuration for read/write (master/replica) splitting.
/// </summary>
/// <remarks>
/// Bind from configuration with
/// <c>services.AddEfCoreReadWriteSplit(configuration.GetSection(ReadWriteOptions.SectionName))</c>
/// or configure inline with the <c>Action&lt;ReadWriteOptions&gt;</c> overload.
/// </remarks>
public sealed class ReadWriteOptions
{
    /// <summary>
    /// The conventional configuration section name: <c>EfCoreReadWriteSplit</c>.
    /// </summary>
    public const string SectionName = "EfCoreReadWriteSplit";

    /// <summary>
    /// Connection string of the primary (master) database. Required.
    /// </summary>
    /// <remarks>
    /// This value is also the connection string handed to the provider when the DbContext is
    /// registered through
    /// <c>services.AddReadWriteDbContext&lt;TContext&gt;(...)</c>, so migrations and design-time tooling always target the master.
    /// </remarks>
    public string WriteConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Connection string of the primary read replica. Required unless at least one entry is added
    /// to <see cref="ReadConnectionStrings"/>.
    /// </summary>
    public string ReadConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Additional read replicas. Load is spread across
    /// <see cref="ReadConnectionString"/> plus these entries by <see cref="IReadReplicaSelector"/>.
    /// </summary>
    public IList<string> ReadConnectionStrings { get; } = new List<string>();

    /// <summary>
    /// The target used when nothing else applies - no <c>[UseReadDb]</c>/<c>[UseWriteDb]</c>
    /// attribute, no <see cref="IDbTargetContext.UseTarget"/> scope and no HTTP verb convention.
    /// Defaults to <see cref="DbTarget.WriteMaster"/>.
    /// </summary>
    /// <remarks>
    /// The master is the default on purpose. Background workers, migrations, health checks and
    /// design-time tooling all run outside the HTTP pipeline, and routing them to a replica by
    /// accident produces read-only failures or silently stale data. Opt into
    /// <see cref="DbTarget.ReadReplica"/> only when you know every unattributed code path is a
    /// lag-tolerant read.
    /// </remarks>
    public DbTarget DefaultTarget { get; set; } = DbTarget.WriteMaster;

    /// <summary>
    /// When <see langword="true"/> (the default), HTTP <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> and
    /// <c>TRACE</c> requests are routed to <see cref="DbTarget.ReadReplica"/> and every other verb
    /// to <see cref="DbTarget.WriteMaster"/>. Explicit attributes always win over this convention.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> to make routing purely explicit, i.e. driven only by
    /// attributes and <see cref="IDbTargetContext.UseTarget"/> scopes.
    /// </remarks>
    public bool RouteByHttpMethod { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), a connection opened while a transaction is active
    /// is forced to <see cref="DbTarget.WriteMaster"/>, whatever the ambient target says.
    /// </summary>
    /// <remarks>
    /// Covers both EF Core transactions (<c>Database.BeginTransaction()</c>) and ambient
    /// <see cref="System.Transactions.TransactionScope"/> transactions. Turning this off lets a
    /// transactional read reach a replica, which fails outright on a read-only node.
    /// </remarks>
    public bool ForceWriteInsideTransaction { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), <c>SaveChanges</c>/<c>SaveChangesAsync</c> forces
    /// <see cref="DbTarget.WriteMaster"/> before touching the database.
    /// </summary>
    /// <remarks>
    /// This is what keeps a <c>GET</c> action that happens to write (an audit row, a
    /// <c>LastSeenAt</c> stamp, a cache fill) from failing against a read-only replica.
    /// </remarks>
    public bool ForceWriteOnSaveChanges { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), the target stays pinned to
    /// <see cref="DbTarget.WriteMaster"/> for the remainder of the current scope after a successful
    /// <c>SaveChanges</c>, giving read-after-write consistency despite replication lag.
    /// </summary>
    /// <remarks>
    /// The pin lasts until the enclosing <see cref="IDbTargetContext.UseTarget"/> scope ends - in a
    /// web application that is the end of the request, in a worker the end of the job scope.
    /// Requires <see cref="ForceWriteOnSaveChanges"/> to be enabled.
    /// </remarks>
    public bool StickToWriteAfterSaveChanges { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), a request for
    /// <see cref="DbTarget.ReadReplica"/> falls back to the master if no replica is configured,
    /// instead of throwing.
    /// </summary>
    public bool AllowReadFallbackToWrite { get; set; } = true;

    /// <summary>
    /// <see cref="Microsoft.AspNetCore.Mvc.Filters.IOrderedFilter.Order"/> given to
    /// <see cref="DbTargetActionFilter"/> when it is registered with
    /// <c>services.AddDbTargetMvcFilter()</c>. Defaults to <see cref="int.MinValue"/>.
    /// </summary>
    /// <remarks>
    /// The lowest possible order makes the filter the outermost one, so the target is already
    /// pinned before any user filter, model binder or action code runs. Raise it if one of your own
    /// filters must run first.
    /// </remarks>
    public int MvcActionFilterOrder { get; set; } = int.MinValue;
}
