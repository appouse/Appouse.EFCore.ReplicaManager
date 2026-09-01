using System;
using System.Threading;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Default <see cref="IReplicaSelector"/>: spreads load evenly across the configured replicas with a
/// lock-free round-robin cursor.
/// <para>
/// TR: Varsayılan <see cref="IReplicaSelector"/> uygulaması: kilitsiz bir round-robin imleciyle yükü
/// tanımlı replica'lara eşit dağıtır.
/// </para>
/// </summary>
/// <remarks>
/// Selection costs a single <see cref="Interlocked.Increment(ref int)"/> and never blocks. Integer
/// overflow is handled by doing the modulo in unsigned arithmetic, which keeps the index in range
/// indefinitely.
/// <para>
/// TR: Seçim tek bir <see cref="Interlocked.Increment(ref int)"/> maliyetindedir ve hiçbir zaman
/// bloklamaz. Tam sayı taşması, modulo işleminin işaretsiz aritmetikle yapılmasıyla ele alınır;
/// böylece indeks süresiz olarak aralıkta kalır.
/// </para>
/// </remarks>
public sealed class RoundRobinReplicaSelector : IReplicaSelector
{
    private int _cursor = -1;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="replicaCount"/> is less than one.
    /// <para>TR: <paramref name="replicaCount"/> birden küçük.</para>
    /// </exception>
    public int SelectStartIndex(int replicaCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(replicaCount, 1);

        if (replicaCount == 1)
        {
            return 0;
        }

        var next = (uint)Interlocked.Increment(ref _cursor);
        return (int)(next % (uint)replicaCount);
    }
}
