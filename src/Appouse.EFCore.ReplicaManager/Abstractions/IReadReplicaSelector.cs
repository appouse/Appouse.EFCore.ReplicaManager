using System.Collections.Generic;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Chooses which read replica to use when more than one is configured.
/// </summary>
/// <remarks>
/// The default implementation is <see cref="RoundRobinReadReplicaSelector"/>. Replace it to
/// implement weighted, latency-aware or health-aware selection:
/// <code>
/// services.Replace(ServiceDescriptor.Singleton&lt;IReadReplicaSelector, WeightedReplicaSelector&gt;());
/// </code>
/// Implementations must be thread-safe and are resolved as singletons.
/// </remarks>
public interface IReadReplicaSelector
{
    /// <summary>
    /// Picks one connection string from <paramref name="replicas"/>.
    /// </summary>
    /// <param name="replicas">
    /// The configured replica connection strings. Never empty; the caller falls back to the master
    /// before calling this method when no replica is configured.
    /// </param>
    /// <returns>The connection string of the replica to use for this operation.</returns>
    string Select(IReadOnlyList<string> replicas);
}
