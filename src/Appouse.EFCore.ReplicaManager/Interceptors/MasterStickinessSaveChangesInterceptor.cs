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
/// <see cref="MasterReplicaOptions.StickToMasterAfterSaveChanges"/> is enabled - that everything read
/// after that write in the same scope reads its own result back.
/// <para>
/// TR: Bir yazmanın her zaman master'a ulaşmasını ve -
/// <see cref="MasterReplicaOptions.StickToMasterAfterSaveChanges"/> etkinken - aynı scope içinde o
/// yazmadan sonra yapılan tüm okumaların kendi sonucunu geri okumasını garanti eder.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// This solves the two failure modes read/write splitting is notorious for. <strong>A replica-routed
/// request that writes:</strong> a <c>GET</c> action that stamps <c>LastSeenAt</c>, appends an audit
/// row or fills a cache would otherwise send an <c>INSERT</c> to a read-only replica. Forcing the
/// target at <c>SavingChanges</c>, before EF Core touches the database, routes the write to the
/// master instead. <strong>Read-after-write across replication lag:</strong> a <c>POST</c> that saves
/// and then re-reads what it just wrote would otherwise read a replica that has not caught up.
/// Pinning the master for the rest of the scope removes the race entirely.
/// </para>
/// <para>
/// TR: Bu, master/replica ayrımının iki klasik hata biçimini çözer. <strong>Replica'ya yönlendirilmiş
/// ama yazan bir istek:</strong> <c>LastSeenAt</c> damgalayan, denetim kaydı ekleyen veya önbellek
/// dolduran bir <c>GET</c> action'ı, aksi hâlde salt okunur bir replica'ya <c>INSERT</c> gönderirdi.
/// Hedefin <c>SavingChanges</c> anında, EF Core veritabanına dokunmadan önce zorlanması yazmayı
/// master'a yönlendirir. <strong>Replikasyon gecikmesi karşısında yazma sonrası okuma:</strong>
/// kaydedip hemen ardından yazdığını tekrar okuyan bir <c>POST</c>, aksi hâlde henüz yetişmemiş bir
/// replica'yı okurdu. Scope'un geri kalanında master'a sabitlemek bu yarışı tamamen ortadan kaldırır.
/// </para>
/// <para>
/// The pin is applied with <see cref="IDbTargetContext.SetTarget"/> rather than
/// <see cref="IDbTargetContext.UseTarget"/> precisely because it must be visible to the
/// <em>callers</em> above this interceptor, and it is undone automatically when the enclosing scope -
/// the request, or the job - ends.
/// </para>
/// <para>
/// TR: Sabitleme, <see cref="IDbTargetContext.UseTarget"/> yerine
/// <see cref="IDbTargetContext.SetTarget"/> ile uygulanır; çünkü tam olarak bu interceptor'ın
/// üstündeki <em>çağıranlar</em> tarafından görülmesi gerekir. Çevreleyen scope - istek veya job -
/// sona erdiğinde kendiliğinden geri alınır.
/// </para>
/// </remarks>
public sealed class MasterStickinessSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// Remembers the target active before a save, so it can be restored afterwards when stickiness is
    /// disabled. Keyed weakly by <see cref="DbContext"/>; EF Core forbids concurrent operations on one
    /// context, so no synchronisation is required.
    /// </summary>
    private readonly ConditionalWeakTable<DbContext, StrongBox<DbTarget>> _previousTargets = new();

    private readonly ILogger<MasterStickinessSaveChangesInterceptor> _logger;
    private readonly MasterReplicaOptions _options;
    private readonly IDbConnectionStringResolver _resolver;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the interceptor.
    /// <para>TR: Interceptor'ı oluşturur.</para>
    /// </summary>
    /// <param name="targetContext">
    /// Ambient store holding the target for the current flow.
    /// <para>TR: Geçerli akışın hedefini tutan ortam deposu.</para>
    /// </param>
    /// <param name="resolver">
    /// Used only to tell whether any replica is actually configured.
    /// <para>TR: Yalnızca gerçekten tanımlı bir replica olup olmadığını anlamak için kullanılır.</para>
    /// </param>
    /// <param name="options">
    /// The configured master/replica options.
    /// <para>TR: Yapılandırılmış master/replica ayarları.</para>
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink.
    /// <para>TR: Tanılama hedefi.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    public MasterStickinessSaveChangesInterceptor(
        IDbTargetContext targetContext,
        IDbConnectionStringResolver resolver,
        IOptions<MasterReplicaOptions> options,
        ILogger<MasterStickinessSaveChangesInterceptor> logger)
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
        ForceMaster(eventData);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ForceMaster(eventData);
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

    private void ForceMaster(DbContextEventData eventData)
    {
        if (!_options.ForceMasterOnSaveChanges)
        {
            return;
        }

        var previous = _targetContext.CurrentTarget;
        if (previous == DbTarget.Master)
        {
            return;
        }

        if (eventData?.Context is { } context)
        {
            _previousTargets.AddOrUpdate(context, new StrongBox<DbTarget>(previous));
            WarnIfConnectionIsAlreadyOpenOnAReplica(context);
        }

        _targetContext.SetTarget(DbTarget.Master);
        Log.SaveChangesForcedMaster(_logger);
    }

    private void RestoreIfNotSticky(DbContext? context)
    {
        if (context is null || !_previousTargets.TryGetValue(context, out var previous))
        {
            return;
        }

        _previousTargets.Remove(context);

        if (!_options.StickToMasterAfterSaveChanges)
        {
            _targetContext.SetTarget(previous.Value);
        }
    }

    /// <summary>
    /// A connection string can only be assigned while the connection is closed. If the context is
    /// already holding an open connection - an explicit transaction, or an explicit
    /// <c>Database.OpenConnection()</c> - the route was fixed before this write was known about, and
    /// forcing the target here cannot move it. Say so loudly instead of letting the provider fail with
    /// an opaque read-only error.
    /// <para>
    /// TR: Bağlantı dizesi yalnızca bağlantı kapalıyken atanabilir. Context zaten açık bir bağlantı
    /// tutuyorsa - açık bir transaction veya açık bir <c>Database.OpenConnection()</c> - rota, bu
    /// yazmadan haberdar olunmadan önce sabitlenmiştir ve hedefi burada zorlamak onu değiştiremez.
    /// Sağlayıcının anlaşılmaz bir salt okunur hatası vermesini beklemek yerine bunu açıkça bildirir.
    /// </para>
    /// </summary>
    /// <param name="context">
    /// The context about to save.
    /// <para>TR: Kaydetmek üzere olan context.</para>
    /// </param>
    private void WarnIfConnectionIsAlreadyOpenOnAReplica(DbContext context)
    {
        if (!context.Database.IsRelational())
        {
            return;
        }

        if (context.Database.GetDbConnection().State == ConnectionState.Closed)
        {
            return;
        }

        // With no replica configured every target resolves to the master anyway, so an open
        // connection is not a problem and the warning would be noise.
        if (_resolver.GetReplicaConnectionStrings().Count == 0)
        {
            return;
        }

        Log.WriteOnOpenReplicaConnection(_logger);
    }
}
