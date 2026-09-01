using System.Threading;

namespace Appouse.EFCore.ReplicaManager.Internal;

/// <summary>
/// Mutable holder for the ambient <see cref="DbTarget"/>, stored inside an
/// <see cref="AsyncLocal{T}"/> by <see cref="DbTargetContext"/>.
/// <para>
/// TR: <see cref="DbTargetContext"/> tarafından bir <see cref="AsyncLocal{T}"/> içinde saklanan,
/// ortam <see cref="DbTarget"/> değerini tutan değiştirilebilir kap.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// The indirection is deliberate and load-bearing. Assigning <c>AsyncLocal&lt;T&gt;.Value</c> only
/// affects the current execution context and everything it subsequently awaits - the change is
/// invisible to the <em>callers</em> above the assigning frame. Storing a mutable object and
/// mutating a field on it means a change made deep in the call stack, such as
/// <see cref="MasterStickinessSaveChangesInterceptor"/> pinning the master from inside
/// <c>SaveChangesAsync</c>, is observed by every frame that shares the same holder.
/// </para>
/// <para>
/// TR: Bu dolaylılık bilinçlidir ve mimarinin taşıyıcı parçasıdır.
/// <c>AsyncLocal&lt;T&gt;.Value</c>'ya atama yapmak yalnızca geçerli yürütme bağlamını ve onun
/// sonrasında beklediği (await) her şeyi etkiler; değişiklik, atamayı yapan çerçevenin üstündeki
/// <em>çağıranlar</em> tarafından görülmez. Değiştirilebilir bir nesne tutup alanını değiştirmek ise,
/// çağrı yığınının derinliğinde yapılan bir değişikliğin - örneğin
/// <see cref="MasterStickinessSaveChangesInterceptor"/>'ın <c>SaveChangesAsync</c> içinden master'ı
/// sabitlemesi - aynı kabı paylaşan tüm çerçeveler tarafından görülmesini sağlar.
/// </para>
/// <para>
/// <see cref="DbTargetContext.UseTarget"/> installs a <em>new</em> holder instead of mutating the
/// existing one, which is what keeps a scope's effect flow-local and undoable.
/// </para>
/// <para>
/// TR: <see cref="DbTargetContext.UseTarget"/> mevcut kabı değiştirmek yerine <em>yeni</em> bir kap
/// kurar; bir scope'un etkisini akışa özel ve geri alınabilir tutan da budur.
/// </para>
/// </remarks>
internal sealed class DbTargetState
{
    /// <summary>
    /// Sentinel meaning "no explicit target for this flow".
    /// <para>TR: "Bu akış için açık bir hedef yok" anlamına gelen nöbetçi değer.</para>
    /// </summary>
    private const int NotSet = -1;

    private int _value;

    /// <summary>
    /// Creates a holder carrying an explicit target.
    /// <para>TR: Açık bir hedef taşıyan kap oluşturur.</para>
    /// </summary>
    /// <param name="target">
    /// The target to start with.
    /// <para>TR: Başlangıç hedefi.</para>
    /// </param>
    internal DbTargetState(DbTarget target) => _value = (int)target;

    /// <summary>
    /// Gets the explicit target for this flow, or <see langword="null"/> when none is set.
    /// <para>
    /// TR: Bu akış için açık hedefi verir; belirlenmemişse <see langword="null"/> döner.
    /// </para>
    /// </summary>
    internal DbTarget? Target
    {
        get
        {
            var value = Volatile.Read(ref _value);
            return value == NotSet ? null : (DbTarget)value;
        }
    }

    /// <summary>
    /// Overwrites the target in place, visible to every frame sharing this holder.
    /// <para>TR: Hedefi yerinde değiştirir; bu kabı paylaşan tüm çerçeveler değişikliği görür.</para>
    /// </summary>
    /// <param name="target">
    /// The new target.
    /// <para>TR: Yeni hedef.</para>
    /// </param>
    internal void Set(DbTarget target) => Volatile.Write(ref _value, (int)target);

    /// <summary>
    /// Clears the target in place, so the configured default applies again.
    /// <para>TR: Hedefi yerinde temizler; böylece yapılandırılmış varsayılan yeniden geçerli olur.</para>
    /// </summary>
    internal void Clear() => Volatile.Write(ref _value, NotSet);
}
