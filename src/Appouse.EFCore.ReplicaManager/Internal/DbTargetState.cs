using System.Threading;

namespace Appouse.EFCore.ReplicaManager.Internal;

/// <summary>
/// Mutable holder for the ambient <see cref="DbTarget"/>, stored inside an
/// <see cref="AsyncLocal{T}"/> by <see cref="DbTargetContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The indirection is deliberate and load-bearing. Assigning <c>AsyncLocal&lt;T&gt;.Value</c>
/// only affects the current execution context and everything it subsequently awaits - the change
/// is invisible to the <em>callers</em> above the assigning frame. Storing a mutable object and
/// mutating a field on it means a change made deep in the call stack (for example
/// <see cref="WriteStickinessSaveChangesInterceptor"/> pinning the master from inside
/// <c>SaveChangesAsync</c>) is observed by every frame that shares the same holder.
/// </para>
/// <para>
/// <see cref="DbTargetContext.UseTarget"/> installs a <em>new</em> holder instead of mutating the
/// existing one, which is what keeps a scope's effect flow-local and undoable.
/// </para>
/// </remarks>
internal sealed class DbTargetState
{
    /// <summary>Sentinel meaning "no explicit target for this flow".</summary>
    private const int NotSet = -1;

    private int _value;

    /// <summary>Creates a holder carrying an explicit target.</summary>
    /// <param name="target">The target to start with.</param>
    internal DbTargetState(DbTarget target) => _value = (int)target;

    /// <summary>
    /// Gets the explicit target for this flow, or <see langword="null"/> when none is set.
    /// </summary>
    internal DbTarget? Target
    {
        get
        {
            var value = Volatile.Read(ref _value);
            return value == NotSet ? null : (DbTarget)value;
        }
    }

    /// <summary>Overwrites the target in place, visible to every frame sharing this holder.</summary>
    /// <param name="target">The new target.</param>
    internal void Set(DbTarget target) => Volatile.Write(ref _value, (int)target);

    /// <summary>Clears the target in place, so the configured default applies again.</summary>
    internal void Clear() => Volatile.Write(ref _value, NotSet);
}
