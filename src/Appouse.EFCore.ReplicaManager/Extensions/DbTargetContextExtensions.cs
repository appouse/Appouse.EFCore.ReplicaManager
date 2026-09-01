using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Readability helpers over <see cref="IDbTargetContext"/>.
/// </summary>
public static class DbTargetContextExtensions
{
    /// <summary>
    /// Pins the current flow to the primary (master) database until the handle is disposed.
    /// </summary>
    /// <param name="context">The ambient target context.</param>
    /// <returns>A handle that restores the previous target when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// using (dbTargetContext.UseWriteDb())
    /// {
    ///     await db.SaveChangesAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    public static IDisposable UseWriteDb(this IDbTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.UseTarget(DbTarget.WriteMaster);
    }

    /// <summary>
    /// Pins the current flow to a read replica until the handle is disposed.
    /// </summary>
    /// <param name="context">The ambient target context.</param>
    /// <returns>A handle that restores the previous target when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static IDisposable UseReadDb(this IDbTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.UseTarget(DbTarget.ReadReplica);
    }
}
