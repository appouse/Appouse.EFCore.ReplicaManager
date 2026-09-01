using System;
using Microsoft.Extensions.Logging;

namespace Appouse.EFCore.ReplicaManager.Internal;

/// <summary>
/// Source-generated, allocation-free log messages for the package.
/// <para>TR: Paket için kaynak üretimli, bellek ayırmayan log mesajları.</para>
/// </summary>
/// <remarks>
/// Connection strings are never logged, at any level: they routinely carry credentials, and a log
/// sink is not a secret store. Only the resolved <see cref="DbTarget"/>, the replica's index in the
/// configured list, and EF Core's own correlation identifiers are emitted.
/// <para>
/// TR: Bağlantı dizeleri hiçbir seviyede loglanmaz: rutin olarak kimlik bilgisi taşırlar ve log
/// hedefleri bir sır deposu değildir. Yalnızca çözümlenen <see cref="DbTarget"/>, replica'nın tanımlı
/// listedeki indeksi ve EF Core'un kendi ilişkilendirme kimlikleri yazılır.
/// </para>
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} routed to the master.")]
    internal static partial void RoutedToMaster(ILogger logger, Guid connectionId);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} opened against replica #{ReplicaIndex}.")]
    internal static partial void OpenedReplica(ILogger logger, Guid connectionId, int replicaIndex);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} is already {State}; its connection string cannot be changed, so the existing route is kept.")]
    internal static partial void ConnectionAlreadyOpen(ILogger logger, Guid connectionId, string state);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "An active transaction forced connection {ConnectionId} to the master.")]
    internal static partial void TransactionForcedMaster(ILogger logger, Guid connectionId);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Debug,
        Message = "SaveChanges forced the ambient target to the master.")]
    internal static partial void SaveChangesForcedMaster(ILogger logger);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "SaveChanges switched the ambient target to the master, but the connection is already open against a replica, so this write will still be sent to the replica. Open the transaction, or the connection, inside an IDbTargetContext.UseTarget(DbTarget.Master) scope.")]
    internal static partial void WriteOnOpenReplicaConnection(ILogger logger);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Warning,
        Message = "Replica #{ReplicaIndex} refused connection {ConnectionId}; failing over to the next replica.")]
    internal static partial void ReplicaOpenFailed(ILogger logger, int replicaIndex, Guid connectionId, Exception exception);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "None of the {ReplicaCount} configured replica(s) accepted connection {ConnectionId}; falling back to the master.")]
    internal static partial void FallingBackToMaster(ILogger logger, int replicaCount, Guid connectionId);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Warning,
        Message = "A replica was requested for connection {ConnectionId} but none is configured; falling back to the master.")]
    internal static partial void NoReplicaConfigured(ILogger logger, Guid connectionId);
}
