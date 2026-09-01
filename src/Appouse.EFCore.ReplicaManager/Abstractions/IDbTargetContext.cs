using System;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Ambient, flow-local store holding the <see cref="DbTarget"/> the current logical operation must
/// use. This is the single source of truth consulted by <see cref="MasterReplicaDbInterceptor"/>
/// when a database connection is about to be opened.
/// <para>
/// TR: Geçerli mantıksal işlemin kullanması gereken <see cref="DbTarget"/> değerini tutan, akışa özel
/// ortam deposu. Bir veritabanı bağlantısı açılmak üzereyken
/// <see cref="MasterReplicaDbInterceptor"/> tarafından danışılan tek doğruluk kaynağıdır.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// The registered implementation is a <em>singleton</em> backed by
/// <see cref="System.Threading.AsyncLocal{T}"/>. It is therefore safe to consume from anywhere - an
/// MVC filter, a middleware, a <c>BackgroundService</c>, a Hangfire job or a Quartz job - and never
/// requires an <c>HttpContext</c>.
/// </para>
/// <para>
/// TR: Kayıtlı uygulama, <see cref="System.Threading.AsyncLocal{T}"/> üzerine kurulu bir
/// <em>singleton</em>'dır. Bu nedenle her yerden - MVC filtresi, middleware,
/// <c>BackgroundService</c>, Hangfire veya Quartz job'ı - güvenle kullanılabilir ve hiçbir zaman
/// <c>HttpContext</c> gerektirmez.
/// </para>
/// <para>
/// State flows <em>downwards</em> through the asynchronous call graph: a target set by a caller is
/// observed by everything it awaits, while a sibling request or job running concurrently on the same
/// thread-pool thread observes its own independent value.
/// </para>
/// <para>
/// TR: Durum, asenkron çağrı grafiğinde <em>aşağı doğru</em> akar: bir çağıranın belirlediği hedefi
/// beklediği (await) her şey görür; aynı thread-pool thread'inde eşzamanlı çalışan başka bir istek
/// veya job ise kendi bağımsız değerini görür.
/// </para>
/// </remarks>
public interface IDbTargetContext
{
    /// <summary>
    /// Gets the target that applies to the current logical flow, falling back to
    /// <see cref="MasterReplicaOptions.DefaultTarget"/> when nothing has been set.
    /// <para>
    /// TR: Geçerli mantıksal akış için geçerli olan hedefi verir; hiçbir şey belirlenmemişse
    /// <see cref="MasterReplicaOptions.DefaultTarget"/> değerine düşer.
    /// </para>
    /// </summary>
    DbTarget CurrentTarget { get; }

    /// <summary>
    /// Gets a value indicating whether the current flow carries an explicit target rather than
    /// falling back to <see cref="MasterReplicaOptions.DefaultTarget"/>.
    /// <para>
    /// TR: Geçerli akışın, varsayılana düşmek yerine açıkça belirlenmiş bir hedef taşıyıp
    /// taşımadığını belirtir.
    /// </para>
    /// </summary>
    bool IsOverridden { get; }

    /// <summary>
    /// Pins the current logical flow to <paramref name="target"/> until the returned handle is
    /// disposed, at which point the previously active target is restored.
    /// <para>
    /// TR: Geçerli mantıksal akışı, döndürülen tutamaç dispose edilene kadar
    /// <paramref name="target"/> hedefine sabitler; dispose edildiğinde önceki hedef geri yüklenir.
    /// </para>
    /// </summary>
    /// <param name="target">
    /// The database to route to inside the scope.
    /// <para>TR: Scope içinde yönlendirilecek veritabanı.</para>
    /// </param>
    /// <returns>
    /// A handle that restores the previous target when disposed.
    /// <para>TR: Dispose edildiğinde önceki hedefi geri yükleyen tutamaç.</para>
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="target"/> is not a defined <see cref="DbTarget"/> value.
    /// <para>TR: <paramref name="target"/> tanımlı bir <see cref="DbTarget"/> değeri değil.</para>
    /// </exception>
    /// <remarks>
    /// Scopes nest, and the returned handle is idempotent: disposing it more than once is a no-op.
    /// Always consume it with a <c>using</c> statement in the <em>same</em> method that starts the
    /// asynchronous work, so the restore happens on the same logical flow.
    /// <para>
    /// TR: Scope'lar iç içe geçebilir ve döndürülen tutamaç idempotenttir: birden fazla kez dispose
    /// edilmesi bir şey değiştirmez. Geri yüklemenin aynı mantıksal akışta gerçekleşmesi için, onu
    /// her zaman asenkron işi başlatan <em>aynı</em> metot içinde <c>using</c> ile kullanın.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using (dbTargetContext.UseTarget(DbTarget.Master))
    /// {
    ///     await dbContext.SaveChangesAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    IDisposable UseTarget(DbTarget target);

    /// <summary>
    /// Overwrites the target for the current flow <em>and every frame that shares it</em>, without
    /// creating a new scope.
    /// <para>
    /// TR: Yeni bir scope açmadan, geçerli akışın <em>ve onu paylaşan tüm çerçevelerin</em> hedefini
    /// değiştirir.
    /// </para>
    /// </summary>
    /// <param name="target">
    /// The database to route to.
    /// <para>TR: Yönlendirilecek veritabanı.</para>
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="target"/> is not a defined <see cref="DbTarget"/> value.
    /// <para>TR: <paramref name="target"/> tanımlı bir <see cref="DbTarget"/> değeri değil.</para>
    /// </exception>
    /// <remarks>
    /// Unlike <see cref="UseTarget"/>, this mutates the ambient state in place, so a change made deep
    /// inside a call stack is also observed by the callers above it. That is what powers
    /// read-after-write consistency; see
    /// <see cref="MasterReplicaOptions.StickToMasterAfterSaveChanges"/>. Prefer
    /// <see cref="UseTarget"/> whenever the change should be undone automatically.
    /// <para>
    /// TR: <see cref="UseTarget"/>'ın aksine bu metot ortam durumunu yerinde değiştirir; böylece
    /// çağrı yığınının derinliğinde yapılan bir değişikliği üstteki çağıranlar da görür. Yazma
    /// sonrası okuma tutarlılığını sağlayan mekanizma budur; bkz.
    /// <see cref="MasterReplicaOptions.StickToMasterAfterSaveChanges"/>. Değişikliğin otomatik geri
    /// alınmasını istiyorsanız <see cref="UseTarget"/> tercih edin.
    /// </para>
    /// </remarks>
    void SetTarget(DbTarget target);

    /// <summary>
    /// Clears any explicit target for the current flow, so <see cref="CurrentTarget"/> falls back to
    /// <see cref="MasterReplicaOptions.DefaultTarget"/> again.
    /// <para>
    /// TR: Geçerli akıştaki açık hedefi temizler; böylece <see cref="CurrentTarget"/> yeniden
    /// <see cref="MasterReplicaOptions.DefaultTarget"/> değerine döner.
    /// </para>
    /// </summary>
    void Reset();
}
