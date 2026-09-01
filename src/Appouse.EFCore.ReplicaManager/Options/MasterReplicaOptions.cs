using System;
using System.Collections.Generic;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Configuration for master/replica database splitting.
/// <para>TR: Master/replica veritabanı ayrımının yapılandırması.</para>
/// </summary>
/// <remarks>
/// Bind from configuration with
/// <c>services.AddEfCoreMasterReplica(configuration.GetSection(MasterReplicaOptions.SectionName))</c>,
/// or configure inline with the <c>Action&lt;MasterReplicaOptions&gt;</c> overload.
/// <para>
/// TR: Yapılandırmadan bağlamak için
/// <c>services.AddEfCoreMasterReplica(configuration.GetSection(MasterReplicaOptions.SectionName))</c>
/// kullanın; veya <c>Action&lt;MasterReplicaOptions&gt;</c> aşırı yüklemesiyle satır içi ayarlayın.
/// </para>
/// </remarks>
public sealed class MasterReplicaOptions
{
    /// <summary>
    /// The conventional configuration section name: <c>EfCoreMasterReplica</c>.
    /// <para>TR: Alışılmış yapılandırma bölümü adı: <c>EfCoreMasterReplica</c>.</para>
    /// </summary>
    public const string SectionName = "EfCoreMasterReplica";

    /// <summary>
    /// Connection string of the single master database. Required.
    /// <para>TR: Tek master veritabanının bağlantı dizesi. Zorunludur.</para>
    /// </summary>
    /// <remarks>
    /// This value is also handed to the provider when the DbContext is registered through
    /// <c>services.AddMasterReplicaDbContext&lt;TContext&gt;(...)</c>, so model building, migrations
    /// and design-time tooling always target the master.
    /// <para>
    /// TR: Bu değer, DbContext <c>services.AddMasterReplicaDbContext&lt;TContext&gt;(...)</c> ile
    /// kaydedildiğinde sağlayıcıya da verilir; böylece model oluşturma, migration'lar ve tasarım
    /// zamanı araçları her zaman master'ı hedefler.
    /// </para>
    /// </remarks>
    public string MasterConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Connection string of the first read replica. Required unless at least one entry is added to
    /// <see cref="ReplicaConnectionStrings"/>.
    /// <para>
    /// TR: İlk okuma replica'sının bağlantı dizesi. <see cref="ReplicaConnectionStrings"/> içine en
    /// az bir kayıt eklenmediyse zorunludur.
    /// </para>
    /// </summary>
    public string ReplicaConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Additional replicas. Load is spread across <see cref="ReplicaConnectionString"/> plus these
    /// entries, and an unreachable replica is skipped in favour of the next one.
    /// <para>
    /// TR: Ek replica'lar. Yük, <see cref="ReplicaConnectionString"/> ile birlikte bu kayıtlara
    /// dağıtılır ve erişilemeyen bir replica atlanarak sıradakine geçilir.
    /// </para>
    /// </summary>
    public IList<string> ReplicaConnectionStrings { get; } = new List<string>();

    /// <summary>
    /// The target used when nothing else applies - no attribute, no <c>UseTarget</c> scope and no
    /// HTTP verb convention. Defaults to <see cref="DbTarget.Master"/>.
    /// <para>
    /// TR: Başka hiçbir kural geçerli olmadığında - attribute yok, <c>UseTarget</c> scope'u yok, HTTP
    /// metodu konvansiyonu yok - kullanılan hedef. Varsayılanı <see cref="DbTarget.Master"/>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The master is the default on purpose. Background workers, migrations, health checks and
    /// design-time tooling all run outside the HTTP pipeline, and routing them to a replica by
    /// accident produces read-only failures or silently stale data.
    /// <para>
    /// TR: Master'ın varsayılan olması bilinçlidir. Arka plan worker'ları, migration'lar, sağlık
    /// kontrolleri ve tasarım zamanı araçları HTTP hattının dışında çalışır; bunların yanlışlıkla
    /// replica'ya yönlendirilmesi salt okunur hatalara veya sessizce bayat veriye yol açar.
    /// </para>
    /// </remarks>
    public DbTarget DefaultTarget { get; set; } = DbTarget.Master;

    /// <summary>
    /// When <see langword="true"/>, HTTP <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> and <c>TRACE</c>
    /// requests are routed to <see cref="DbTarget.Replica"/> and every other verb to
    /// <see cref="DbTarget.Master"/>. <strong>Off by default.</strong>
    /// <para>
    /// TR: <see langword="true"/> ise HTTP <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> ve <c>TRACE</c>
    /// istekleri <see cref="DbTarget.Replica"/>, diğer tüm metotlar <see cref="DbTarget.Master"/>
    /// hedefine yönlendirilir. <strong>Varsayılan olarak kapalıdır.</strong>
    /// </para>
    /// </summary>
    /// <remarks>
    /// Routing is explicit by default: a request with no <c>[UseMasterDb]</c>/<c>[UseReplicaDb]</c>
    /// attribute and no <see cref="IDbTargetContext.UseTarget"/> scope goes to
    /// <see cref="DefaultTarget"/> and nowhere else. Adding this package to an existing application
    /// therefore changes nothing until you ask it to, which is the point: a <c>GET</c> is not
    /// reliably a read, and silently moving every one of them onto a replica is a behaviour change
    /// no one asked for.
    /// <para>
    /// TR: Yönlendirme varsayılan olarak açıktır ve yalnızca söylediğinizi yapar:
    /// <c>[UseMasterDb]</c>/<c>[UseReplicaDb]</c> attribute'u ve
    /// <see cref="IDbTargetContext.UseTarget"/> scope'u olmayan bir istek
    /// <see cref="DefaultTarget"/> hedefine gider, başka hiçbir yere değil. Bu yüzden paketi mevcut
    /// bir uygulamaya eklemek, siz istemedikçe hiçbir şeyi değiştirmez. Amaç da budur: bir
    /// <c>GET</c> her zaman okuma anlamına gelmez ve hepsini sessizce replica'ya taşımak, kimsenin
    /// istemediği bir davranış değişikliğidir.
    /// </para>
    /// <para>
    /// Turn it on once you have satisfied yourself that every unattributed <c>GET</c> really is a
    /// lag-tolerant read.
    /// </para>
    /// <para>
    /// TR: İşaretlenmemiş her <c>GET</c>'in gerçekten gecikmeye dayanıklı bir okuma olduğuna ikna
    /// olduktan sonra açın.
    /// </para>
    /// </remarks>
    public bool RouteByHttpMethod { get; set; }

    /// <summary>
    /// When <see langword="true"/> (the default), a connection opened while a transaction is active
    /// is forced to <see cref="DbTarget.Master"/>, whatever the ambient target says.
    /// <para>
    /// TR: <see langword="true"/> ise (varsayılan), bir transaction etkinken açılan bağlantı, ortam
    /// hedefi ne derse desin <see cref="DbTarget.Master"/> hedefine zorlanır.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Covers both EF Core transactions and ambient
    /// <see cref="System.Transactions.TransactionScope"/> transactions.
    /// <para>
    /// TR: Hem EF Core transaction'larını hem de ortam
    /// <see cref="System.Transactions.TransactionScope"/> transaction'larını kapsar.
    /// </para>
    /// </remarks>
    public bool ForceMasterInsideTransaction { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), <c>SaveChanges</c> switches to
    /// <see cref="DbTarget.Master"/> before touching the database.
    /// <para>
    /// TR: <see langword="true"/> ise (varsayılan), <c>SaveChanges</c> veritabanına dokunmadan önce
    /// <see cref="DbTarget.Master"/> hedefine geçer.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This keeps a <c>GET</c> action that happens to write - an audit row, a <c>LastSeenAt</c>
    /// stamp, a cache fill - from failing against a read-only replica.
    /// <para>
    /// TR: Bu, yazma da yapan bir <c>GET</c> action'ının - denetim kaydı, <c>LastSeenAt</c> damgası,
    /// önbellek doldurma - salt okunur bir replica'da hata vermesini engeller.
    /// </para>
    /// </remarks>
    public bool ForceMasterOnSaveChanges { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), the target stays pinned to
    /// <see cref="DbTarget.Master"/> for the remainder of the current scope after a successful
    /// <c>SaveChanges</c>, giving read-after-write consistency despite replication lag.
    /// <para>
    /// TR: <see langword="true"/> ise (varsayılan), başarılı bir <c>SaveChanges</c> sonrasında hedef
    /// geçerli scope'un sonuna kadar <see cref="DbTarget.Master"/> hedefine sabit kalır; böylece
    /// replikasyon gecikmesine rağmen yazma sonrası okuma tutarlılığı sağlanır.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The pin lasts until the enclosing scope ends - in a web application the end of the request,
    /// in a worker the end of the job scope. Requires
    /// <see cref="ForceMasterOnSaveChanges"/> to be enabled.
    /// <para>
    /// TR: Sabitleme, çevreleyen scope bitene kadar sürer: web uygulamasında isteğin sonu, worker'da
    /// job scope'unun sonu. <see cref="ForceMasterOnSaveChanges"/> etkin olmalıdır.
    /// </para>
    /// </remarks>
    public bool StickToMasterAfterSaveChanges { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), a request for <see cref="DbTarget.Replica"/> falls
    /// back to the master if no replica is configured, or if every configured replica refused the
    /// connection.
    /// <para>
    /// TR: <see langword="true"/> ise (varsayılan), <see cref="DbTarget.Replica"/> talebi, hiç
    /// replica tanımlı değilse veya tanımlı tüm replica'lar bağlantıyı reddettiyse master'a düşer.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> to make a total replica outage fail loudly instead of shifting
    /// read traffic onto the master, which may be the safer choice when the master cannot absorb it.
    /// <para>
    /// TR: Tüm replica'ların çökmesi durumunda okuma trafiğini master'a kaydırmak yerine açıkça hata
    /// vermesi için <see langword="false"/> yapın; master bu yükü kaldıramayacaksa daha güvenli
    /// seçenek budur.
    /// </para>
    /// </remarks>
    public bool AllowReplicaFallbackToMaster { get; set; } = true;

    /// <summary>
    /// How long a replica is skipped after it refuses a connection. Defaults to 30 seconds.
    /// <para>
    /// TR: Bir replica bağlantıyı reddettikten sonra ne kadar süre atlanacağı. Varsayılanı 30
    /// saniyedir.
    /// </para>
    /// </summary>
    /// <remarks>
    /// While a replica is cooling down it is moved to the back of the queue rather than banned: if
    /// every other replica also fails, it is still tried. Set to <see cref="TimeSpan.Zero"/> to
    /// disable the cooldown and retry every replica on every connection.
    /// <para>
    /// TR: Bekleme süresi boyunca replica yasaklanmaz, yalnızca sıranın sonuna alınır: diğer tüm
    /// replica'lar da başarısız olursa yine denenir. Bekleme süresini kapatıp her bağlantıda tüm
    /// replica'ları yeniden denemek için <see cref="TimeSpan.Zero"/> verin.
    /// </para>
    /// </remarks>
    public TimeSpan ReplicaFailureCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <see langword="true"/> (the default), the host checks at start-up that the package is
    /// actually wired into the application, instead of silently doing nothing.
    /// <para>
    /// TR: <see langword="true"/> ise (varsayılan), paketin uygulamaya gerçekten bağlanmış olduğu
    /// açılışta denetlenir; sessizce hiçbir şey yapmaması engellenir.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Two things are checked. A registered <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
    /// without the routing interceptor throws, because the package is doing nothing at all for it.
    /// An application that registered controllers but neither
    /// <c>services.AddDbTargetMvcFilter()</c> nor <c>app.UseDbTargetRouting()</c> gets a warning,
    /// because routing purely through <see cref="IDbTargetContext.UseTarget"/> scopes is a
    /// legitimate choice. Requires an <c>IHost</c>; a bare <c>ServiceProvider</c> runs no hosted
    /// services and so runs no checks.
    /// <para>
    /// TR: İki şey denetlenir. Yönlendirme interceptor'ı olmadan kaydedilmiş bir
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> hata fırlatır; çünkü paket onun için
    /// hiçbir şey yapmıyordur. Controller kaydedip ne <c>services.AddDbTargetMvcFilter()</c> ne de
    /// <c>app.UseDbTargetRouting()</c> çağırmış bir uygulama uyarı alır; çünkü yalnızca
    /// <see cref="IDbTargetContext.UseTarget"/> scope'larıyla yönlendirme yapmak meşru bir
    /// tercihtir. Bir <c>IHost</c> gerektirir; çıplak bir <c>ServiceProvider</c> hosted service
    /// çalıştırmadığı için denetim de yapmaz.
    /// </para>
    /// </remarks>
    public bool ValidateStartupWiring { get; set; } = true;

    /// <summary>
    /// Context types that are deliberately left unrouted, and so must not trip
    /// <see cref="ValidateStartupWiring"/>.
    /// <para>
    /// TR: Bilinçli olarak yönlendirilmemiş bırakılan ve bu yüzden
    /// <see cref="ValidateStartupWiring"/> denetimine takılmaması gereken context türleri.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Use it for a second context that lives on an unrelated database - an outbox, an audit log, a
    /// job store - which has no master and no replica of its own.
    /// <para>
    /// TR: Kendi master'ı ve replica'sı olmayan, ilgisiz bir veritabanında yaşayan ikinci bir
    /// context için kullanın: outbox, denetim kaydı, job deposu gibi.
    /// </para>
    /// </remarks>
    public IList<Type> UnroutedDbContextTypes { get; } = new List<Type>();

    /// <summary>
    /// Order given to <see cref="DbTargetActionFilter"/> when it is registered with
    /// <c>services.AddDbTargetMvcFilter()</c>. Defaults to <see cref="int.MinValue"/>.
    /// <para>
    /// TR: <c>services.AddDbTargetMvcFilter()</c> ile kaydedildiğinde
    /// <see cref="DbTargetActionFilter"/> filtresine verilen sıra değeri. Varsayılanı
    /// <see cref="int.MinValue"/>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The lowest possible order makes the filter the outermost one, so the target is pinned before
    /// any user filter, model binder or action code runs.
    /// <para>
    /// TR: Mümkün olan en düşük sıra filtreyi en dışa alır; böylece hedef, kullanıcı filtreleri,
    /// model binder'lar ve action kodu çalışmadan önce sabitlenir.
    /// </para>
    /// </remarks>
    public int MvcActionFilterOrder { get; set; } = int.MinValue;
}
