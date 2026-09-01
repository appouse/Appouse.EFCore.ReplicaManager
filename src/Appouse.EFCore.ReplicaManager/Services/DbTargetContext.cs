using System;
using System.Threading;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Default <see cref="IDbTargetContext"/>: a thread-safe, scope-safe, <c>HttpContext</c>-free ambient
/// store built on <see cref="AsyncLocal{T}"/>.
/// <para>
/// TR: Varsayılan <see cref="IDbTargetContext"/> uygulaması: <see cref="AsyncLocal{T}"/> üzerine
/// kurulu, thread-safe, scope-safe ve <c>HttpContext</c> gerektirmeyen ortam deposu.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Registered as a <em>singleton</em>. That is not a compromise, it is the correct lifetime:
/// <see cref="MasterReplicaDbInterceptor"/> is captured inside <c>DbContextOptions</c>, which EF Core
/// caches for the lifetime of the application. A scoped context, or a scoped interceptor holding
/// one, would produce a different options instance per scope and defeat EF Core's internal
/// service-provider cache. All per-flow state lives in the <see cref="AsyncLocal{T}"/> rather than in
/// the instance, so a singleton is both correct and allocation-free at rest.
/// </para>
/// <para>
/// TR: <em>Singleton</em> olarak kaydedilir. Bu bir ödün değil, doğru yaşam süresidir:
/// <see cref="MasterReplicaDbInterceptor"/>, EF Core'un uygulama ömrü boyunca önbelleklediği
/// <c>DbContextOptions</c> içinde tutulur. Scoped bir context - veya onu tutan scoped bir interceptor -
/// her scope için farklı bir options örneği üretir ve EF Core'un iç servis sağlayıcı önbelleğini
/// işlevsiz bırakır. Akışa özel durumun tamamı örnekte değil <see cref="AsyncLocal{T}"/> içinde
/// yaşadığından, singleton hem doğrudur hem de boştayken hiç bellek ayırmaz.
/// </para>
/// <para>
/// Values never leak between concurrent requests or jobs: each runs on its own
/// <see cref="ExecutionContext"/>, and <see cref="AsyncLocal{T}"/> assignments are copy-on-write per
/// context rather than per thread, so thread-pool thread reuse is irrelevant.
/// </para>
/// <para>
/// TR: Değerler eşzamanlı istekler veya job'lar arasında sızmaz: her biri kendi
/// <see cref="ExecutionContext"/> bağlamında çalışır ve <see cref="AsyncLocal{T}"/> atamaları
/// thread başına değil bağlam başına kopyala-yaz mantığıyla işler; bu yüzden thread-pool
/// thread'lerinin yeniden kullanılması sonucu etkilemez.
/// </para>
/// </remarks>
public sealed class DbTargetContext : IDbTargetContext
{
    private readonly AsyncLocal<DbTargetState?> _state = new();
    private readonly MasterReplicaOptions _options;

    /// <summary>
    /// Creates a context bound to the supplied options.
    /// <para>TR: Verilen ayarlara bağlı bir context oluşturur.</para>
    /// </summary>
    /// <param name="options">
    /// The configured master/replica options.
    /// <para>TR: Yapılandırılmış master/replica ayarları.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="options"/> <see langword="null"/>.</para>
    /// </exception>
    public DbTargetContext(IOptions<MasterReplicaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Creates a context bound to the supplied options. Convenient for unit tests that do not want an
    /// options pipeline.
    /// <para>
    /// TR: Verilen ayarlara bağlı bir context oluşturur. Options hattı kurmak istemeyen birim
    /// testleri için pratiktir.
    /// </para>
    /// </summary>
    /// <param name="options">
    /// The master/replica options.
    /// <para>TR: Master/replica ayarları.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="options"/> <see langword="null"/>.</para>
    /// </exception>
    public DbTargetContext(MasterReplicaOptions options)
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
    /// <c>Dispose</c> cannot corrupt the ambient state.
    /// <para>
    /// TR: Dispose edildiğinde önceki kabı geri yükler. Dispose idempotenttir; iki kez çağrılması
    /// ortam durumunu bozamaz.
    /// </para>
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
