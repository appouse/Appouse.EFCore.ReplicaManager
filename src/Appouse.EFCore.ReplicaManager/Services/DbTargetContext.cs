using System;
using System.Threading;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Default <see cref="IDbTargetContext"/>: a thread-safe, scope-safe, <c>HttpContext</c>-free
/// ambient store built on <see cref="AsyncLocal{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a <em>singleton</em>. That is not a compromise, it is the correct lifetime:
/// <see cref="ReadWriteDbInterceptor"/> is captured inside <c>DbContextOptions</c>, which EF Core
/// caches for the lifetime of the application. A scoped context (or a scoped interceptor holding
/// one) would produce a different options instance per scope, defeating EF Core's internal service
/// provider cache. All per-flow state lives in the <see cref="AsyncLocal{T}"/>, not in the
/// instance, so a singleton is both correct and allocation-free at rest.
/// </para>
/// <para>
/// Values never leak between concurrent requests or jobs: each one runs on its own
/// <see cref="System.Threading.ExecutionContext"/>, and <see cref="AsyncLocal{T}"/> assignments are
/// copy-on-write per context rather than per thread. Thread-pool thread reuse is therefore
/// irrelevant.
/// </para>
/// </remarks>
public sealed class DbTargetContext : IDbTargetContext
{
    private readonly AsyncLocal<DbTargetState?> _state = new();
    private readonly ReadWriteOptions _options;

    /// <summary>
    /// Creates a context bound to the supplied options.
    /// </summary>
    /// <param name="options">The configured read/write splitting options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public DbTargetContext(IOptions<ReadWriteOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Creates a context bound to the supplied options. Convenient for unit tests that do not want
    /// an options pipeline.
    /// </summary>
    /// <param name="options">The read/write splitting options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public DbTargetContext(ReadWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public DbTarget CurrentTarget => _state.Value?.Target ?? _options.DefaultTarget;

    /// <inheritdoc />
    public bool IsOverridden => _state.Value?.Target is not null;

    /// <inheritdoc />
    public IDisposable UseTarget(DbTarget target)
    {
        ThrowIfUndefined(target);

        var previous = _state.Value;
        _state.Value = new DbTargetState(target);
        return new TargetScope(this, previous);
    }

    /// <inheritdoc />
    public void SetTarget(DbTarget target)
    {
        ThrowIfUndefined(target);

        var state = _state.Value;
        if (state is null)
        {
            _state.Value = new DbTargetState(target);
        }
        else
        {
            state.Set(target);
        }
    }

    /// <inheritdoc />
    public void Reset() => _state.Value?.Clear();

    private static void ThrowIfUndefined(DbTarget target)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"'{(int)target}' is not a defined {nameof(DbTarget)} value.");
        }
    }

    /// <summary>
    /// Restores the previously active holder when disposed. Disposal is idempotent, so a double
    /// <c>Dispose</c> (or a <c>using</c> plus an explicit call) cannot corrupt the ambient state.
    /// </summary>
    private sealed class TargetScope : IDisposable
    {
        private readonly DbTargetState? _previous;
        private DbTargetContext? _owner;

        internal TargetScope(DbTargetContext owner, DbTargetState? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
            {
                owner._state.Value = _previous;
            }
        }
    }
}
