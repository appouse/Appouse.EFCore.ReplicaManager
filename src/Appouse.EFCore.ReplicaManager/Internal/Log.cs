using System;
using Microsoft.Extensions.Logging;

namespace Appouse.EFCore.ReplicaManager.Internal;

/// <summary>
/// Source-generated, allocation-free log messages for the package.
/// </summary>
/// <remarks>
/// Connection strings are never logged, at any level: they routinely carry credentials, and log
/// sinks are not a secret store. Only the resolved <see cref="DbTarget"/> and EF Core's own
/// correlation identifiers are emitted.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} routed to {Target}.")]
    internal static partial void ConnectionRouted(ILogger logger, Guid connectionId, DbTarget target);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} is already {State}; its connection string cannot be changed and the existing route is kept.")]
    internal static partial void ConnectionAlreadyOpen(ILogger logger, Guid connectionId, string state);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Resolved an empty connection string for {Target}; the connection is left untouched. Check your ReadWriteOptions configuration.")]
    internal static partial void EmptyConnectionString(ILogger logger, DbTarget target);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "An active transaction forced connection {ConnectionId} to {Target}.")]
    internal static partial void TransactionForcedWrite(ILogger logger, Guid connectionId, DbTarget target);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Debug,
        Message = "SaveChanges forced the ambient target to {Target}.")]
    internal static partial void SaveChangesForcedWrite(ILogger logger, DbTarget target);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "SaveChanges switched the ambient target to WriteMaster, but the connection is already open against a read replica, so this write will still be sent to the replica. Open the transaction (or the connection) inside an IDbTargetContext.UseTarget(DbTarget.WriteMaster) scope.")]
    internal static partial void WriteOnOpenReplicaConnection(ILogger logger);
}
