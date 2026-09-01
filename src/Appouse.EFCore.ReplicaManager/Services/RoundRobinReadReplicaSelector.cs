using System;
using System.Collections.Generic;
using System.Threading;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Default <see cref="IReadReplicaSelector"/>: spreads load evenly across the configured replicas
/// with a lock-free round-robin cursor.
/// </summary>
/// <remarks>
/// The cursor is a single <see cref="Interlocked.Increment(ref int)"/>, so selection costs one
/// atomic operation and never blocks. Integer overflow is handled by doing the modulo in unsigned
/// arithmetic, which keeps the index in range forever.
/// </remarks>
public sealed class RoundRobinReadReplicaSelector : IReadReplicaSelector
{
    private int _cursor = -1;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="replicas"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="replicas"/> is empty.</exception>
    public string Select(IReadOnlyList<string> replicas)
    {
        ArgumentNullException.ThrowIfNull(replicas);

        if (replicas.Count == 0)
        {
            throw new ArgumentException("At least one replica connection string is required.", nameof(replicas));
        }

        if (replicas.Count == 1)
        {
            return replicas[0];
        }

        var next = (uint)Interlocked.Increment(ref _cursor);
        return replicas[(int)(next % (uint)replicas.Count)];
    }
}
