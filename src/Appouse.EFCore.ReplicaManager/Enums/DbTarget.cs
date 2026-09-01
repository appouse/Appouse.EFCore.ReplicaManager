namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Identifies the physical database a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// operation must be routed to.
/// </summary>
public enum DbTarget
{
    /// <summary>
    /// The primary (master) database. Every write, and every read that must observe the most
    /// recent write, is routed here.
    /// </summary>
    /// <remarks>
    /// This member is deliberately <c>0</c> so that <c>default(DbTarget)</c> resolves to the
    /// master. An unresolved or uninitialised target must never silently fall through to a
    /// replica: the master is the only node guaranteed to be both readable and writable.
    /// </remarks>
    WriteMaster = 0,

    /// <summary>
    /// A read-only replica. Safe only for queries that tolerate replication lag.
    /// </summary>
    ReadReplica = 1,
}
