using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Ambient, flow-local store for the <see cref="DbTarget"/> that the current logical operation
/// must use. This is the single source of truth consulted by
/// <see cref="ReadWriteDbInterceptor"/> when a database connection is about to be opened.
/// </summary>
/// <remarks>
/// <para>
/// The implementation registered by <c>services.AddEfCoreReadWriteSplit(...)</c>
/// is a <em>singleton</em> backed by <see cref="System.Threading.AsyncLocal{T}"/>. It is therefore
/// safe to consume from anywhere - an MVC filter, a middleware, a <c>BackgroundService</c>, a
/// Hangfire job or a Quartz job - and it never requires an <c>HttpContext</c>.
/// </para>
/// <para>
/// State flows <em>downwards</em> through the asynchronous call graph: a target set by a caller is
/// observed by everything it awaits, while a sibling request or job running concurrently on the
/// same thread pool thread observes its own, independent value.
/// </para>
/// </remarks>
public interface IDbTargetContext
{
    /// <summary>
    /// Gets the target that applies to the current logical flow, falling back to
    /// <see cref="ReadWriteOptions.DefaultTarget"/> when nothing has been set.
    /// </summary>
    DbTarget CurrentTarget { get; }

    /// <summary>
    /// Gets a value indicating whether the current flow carries an explicit target
    /// (set through <see cref="UseTarget"/> or <see cref="SetTarget"/>) rather than falling back
    /// to <see cref="ReadWriteOptions.DefaultTarget"/>.
    /// </summary>
    bool IsOverridden { get; }

    /// <summary>
    /// Pins the current logical flow to <paramref name="target"/> until the returned handle is
    /// disposed, at which point the previously active target is restored.
    /// </summary>
    /// <param name="target">The database to route to inside the scope.</param>
    /// <returns>A handle that restores the previous target when disposed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="target"/> is not a defined <see cref="DbTarget"/> value.
    /// </exception>
    /// <remarks>
    /// Scopes nest, and the returned handle is idempotent: disposing it more than once is a no-op.
    /// Always consume it with a <c>using</c> statement in the <em>same</em> method that starts the
    /// asynchronous work, so that the restore happens on the same logical flow.
    /// </remarks>
    /// <example>
    /// <code>
    /// using (dbTargetContext.UseTarget(DbTarget.WriteMaster))
    /// {
    ///     await dbContext.SaveChangesAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    IDisposable UseTarget(DbTarget target);

    /// <summary>
    /// Overwrites the target for the current flow <em>and every frame that shares it</em>, without
    /// creating a new scope.
    /// </summary>
    /// <param name="target">The database to route to.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="target"/> is not a defined <see cref="DbTarget"/> value.
    /// </exception>
    /// <remarks>
    /// Unlike <see cref="UseTarget"/>, this mutates the ambient state in place, so a change made
    /// deep inside a call stack is also observed by the callers above it. That is what powers
    /// read-after-write consistency (see <see cref="ReadWriteOptions.StickToWriteAfterSaveChanges"/>).
    /// Prefer <see cref="UseTarget"/> whenever you want the change to be undone automatically.
    /// </remarks>
    void SetTarget(DbTarget target);

    /// <summary>
    /// Clears any explicit target for the current flow, so that
    /// <see cref="CurrentTarget"/> falls back to <see cref="ReadWriteOptions.DefaultTarget"/> again.
    /// </summary>
    void Reset();
}
