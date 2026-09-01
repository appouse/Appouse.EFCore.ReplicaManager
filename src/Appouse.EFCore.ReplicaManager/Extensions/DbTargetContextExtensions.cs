using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Readability helpers over <see cref="IDbTargetContext"/>.
/// <para>TR: <see cref="IDbTargetContext"/> için okunabilirlik yardımcıları.</para>
/// </summary>
public static class DbTargetContextExtensions
{
    /// <summary>
    /// Pins the current flow to the master database until the handle is disposed.
    /// <para>TR: Tutamaç dispose edilene kadar geçerli akışı master veritabanına sabitler.</para>
    /// </summary>
    /// <param name="context">
    /// The ambient target context.
    /// <para>TR: Ortam hedef context'i.</para>
    /// </param>
    /// <returns>
    /// A handle that restores the previous target when disposed.
    /// <para>TR: Dispose edildiğinde önceki hedefi geri yükleyen tutamaç.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="context"/> <see langword="null"/>.</para>
    /// </exception>
    /// <example>
    /// <code>
    /// using (dbTargetContext.UseMasterDb())
    /// {
    ///     await db.SaveChangesAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    public static IDisposable UseMasterDb(this IDbTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.UseTarget(DbTarget.Master);
    }

    /// <summary>
    /// Pins the current flow to a read replica until the handle is disposed.
    /// <para>TR: Tutamaç dispose edilene kadar geçerli akışı bir okuma replica'sına sabitler.</para>
    /// </summary>
    /// <param name="context">
    /// The ambient target context.
    /// <para>TR: Ortam hedef context'i.</para>
    /// </param>
    /// <returns>
    /// A handle that restores the previous target when disposed.
    /// <para>TR: Dispose edildiğinde önceki hedefi geri yükleyen tutamaç.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="context"/> <see langword="null"/>.</para>
    /// </exception>
    public static IDisposable UseReplicaDb(this IDbTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.UseTarget(DbTarget.Replica);
    }
}
