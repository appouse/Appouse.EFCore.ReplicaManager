using System;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Guarantees that a write always reaches the master, and - when
/// <see cref="ReadWriteOptions.StickToWriteAfterSaveChanges"/> is enabled - that everything read
/// after that write in the same scope reads its own result back.
/// </summary>
/// <remarks>
/// <para>
/// This solves the two failure modes that read/write splitting is notorious for.
/// </para>
/// <para>
/// <strong>A read-routed request that writes.</strong> A <c>GET</c> action that stamps
/// <c>LastSeenAt</c>, appends an audit row or fills a cache would otherwise send an <c>INSERT</c>
/// to a read-only replica. Forcing the target at <c>SavingChanges</c>, before EF Core touches the
/// database, routes the write to the master instead.
/// </para>
/// <para>
/// <strong>Read-after-write across replication lag.</strong> A <c>POST</c> that saves and then
/// re-reads what it just wrote - or a client that immediately issues a follow-up <c>GET</c> in the
/// same request - would read a replica that has not caught up yet. Pinning the master for the rest
/// of the scope removes the race entirely.
/// </para>
/// <para>
/// The pin is applied with <see cref="IDbTargetContext.SetTarget"/> rather than
/// <see cref="IDbTargetContext.UseTarget"/> precisely because it must be visible to the
/// <em>callers</em> above this interceptor, and it is undone automatically when the enclosing
/// scope (the request, or the job) ends.
/// </para>
/// </remarks>
public sealed class WriteStickinessSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// Remembers the target that was active before a save, so it can be restored afterwards when
    /// stickiness is disabled. Keyed weakly by <see cref="DbContext"/>; EF Core forbids concurrent
    /// operations on one context, so no synchronisation is required.
    /// </summary>
    private readonly ConditionalWeakTable<DbContext, StrongBox<DbTarget>> _previousTargets = new();

    private readonly ILogger<WriteStickinessSaveChangesInterceptor> _logger;
    private readonly ReadWriteOptions _options;
    private readonly IDbConnectionStringResolver _resolver;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the interceptor.
    /// </summary>
    /// <param name="targetContext">Ambient store holding the target for the current flow.</param>
    /// <param name="resolver">Used only to tell whether a distinct replica is actually configured.</param>
    /// <param name="options">The configured read/write splitting options.</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public WriteStickinessSaveChangesInterceptor(
        IDbTargetContext targetContext,
        IDbConnectionStringResolver resolver,
        IOptions<ReadWriteOptions> options,
        ILogger<WriteStickinessSaveChangesInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _targetContext = targetContext;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ForceWriteMaster(eventData);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ForceWriteMaster(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        RestoreIfNotSticky(eventData?.Context);
        return base.SavedChanges(eventData!, result);
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        RestoreIfNotSticky(eventData?.Context);
        return base.SavedChangesAsync(eventData!, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        RestoreIfNotSticky(eventData?.Context);
        base.SaveChangesFailed(eventData!);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RestoreIfNotSticky(eventData?.Context);
        return base.SaveChangesFailedAsync(eventData!, cancellationToken);
    }

    private void ForceWriteMaster(DbContextEventData eventData)
    {
        if (!_options.ForceWriteOnSaveChanges)
        {
            return;
        }

        var previous = _targetContext.CurrentTarget;
        if (previous == DbTarget.WriteMaster)
        {
            return;
        }

        if (eventData?.Context is { } context)
        {
            _previousTargets.AddOrUpdate(context, new StrongBox<DbTarget>(previous));
            WarnIfConnectionIsAlreadyOpenOnAReplica(context);
        }

        _targetContext.SetTarget(DbTarget.WriteMaster);
        Log.SaveChangesForcedWrite(_logger, DbTarget.WriteMaster);
    }

    private void RestoreIfNotSticky(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        if (!_previousTargets.TryGetValue(context, out var previous))
        {
            return;
        }

        _previousTargets.Remove(context);

        if (!_options.StickToWriteAfterSaveChanges)
        {
            _targetContext.SetTarget(previous.Value);
        }
    }

    /// <summary>
    /// A connection string can only be assigned while the connection is closed. If the context is
    /// already holding an open connection - an explicit transaction, or an explicit
    /// <c>Database.OpenConnection()</c> - the route was fixed before this write was known about,
    /// and forcing the target here cannot move it. Say so loudly instead of letting the provider
    /// fail with an opaque read-only error.
    /// </summary>
    private void WarnIfConnectionIsAlreadyOpenOnAReplica(DbContext context)
    {
        if (!context.Database.IsRelational())
        {
            return;
        }

        var connection = context.Database.GetDbConnection();
        if (connection.State == ConnectionState.Closed)
        {
            return;
        }

        // When no distinct replica is configured every target resolves to the master anyway,
        // so an open connection is not a problem and the warning would be noise.
        if (string.Equals(
                _resolver.Resolve(DbTarget.ReadReplica),
                _resolver.Resolve(DbTarget.WriteMaster),
                StringComparison.Ordinal))
        {
            return;
        }

        Log.WriteOnOpenReplicaConnection(_logger);
    }
}
