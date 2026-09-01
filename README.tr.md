# Appouse.EFCore.ReplicaManager

[English](https://github.com/appouse/Appouse.EFCore.ReplicaManager/blob/main/README.md) · **Türkçe**

**EF Core 8+** için şeffaf **master/replica** veritabanı ayrımı.

Tek master, istediğiniz kadar replica. Hangi sorgunun nereye gideceğini siz söylersiniz — bir
attribute, bir `using` bloğu ya da bir varsayılanla — ve yanıt vermeyen bir replica atlanıp sıradakine
geçilir. Sağlayıcıdan bağımsız: SQL Server, PostgreSQL, MySQL, Oracle, SQLite ve EF Core sağlayıcısı
olan her şey.

```bash
dotnet add package Appouse.EFCore.ReplicaManager
```

**Bu paketi eklemek kendiliğinden hiçbir trafiği taşımaz.** Yönlendirme açıktır ve yalnızca
söylediğinizi yapar. Attribute'u ve scope'u olmayan bir sorgu `DefaultTarget` hedefine gider, başka
hiçbir yere değil. Böylece paketi çalışan bir uygulamaya kurup yönlendirmeyi endpoint endpoint
açabilirsiniz.

---

## 30 saniye

```csharp
using Appouse.EFCore.ReplicaManager;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEfCoreMasterReplica(options =>
{
    options.MasterConnectionString  = builder.Configuration.GetConnectionString("Master")!;
    options.ReplicaConnectionString = builder.Configuration.GetConnectionString("Replica1")!;
    options.ReplicaConnectionStrings.Add(builder.Configuration.GetConnectionString("Replica2")!);
    options.DefaultTarget = DbTarget.Master;
});

// Sağlayıcıyı siz seçersiniz; paket bağlantı dizesini size verir.
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, connectionString) =>
    options.UseSqlServer(connectionString));

builder.Services.AddControllers().AddDbTargetRouting();

var app = builder.Build();
app.MapControllers();
app.Run();
```

`DbContext`'inizde tek satır değişmez. Ne taban sınıf, ne ikinci context, ne repository katmanı.

---

## Yönlendirme kuralları

Şu sırayla çözümlenir — ilk eşleşen kazanır:

| # | Kural | Sonuç |
|---|------|--------|
| 1 | **Action** üzerinde `[UseMasterDb]` / `[UseReplicaDb]` | o hedef |
| 2 | **Controller** üzerinde `[UseMasterDb]` / `[UseReplicaDb]` | o hedef |
| 3 | `SaveChanges` çalışıyor | **her zaman** `Master` |
| 4 | Aktif transaction var | **her zaman** `Master` |
| 5 | Çevreleyen `UseTarget(...)` scope'u | o hedef |
| 6 | HTTP metodu — *yalnızca `RouteByHttpMethod` açıksa* | `GET`/`HEAD`/`OPTIONS`/`TRACE` → `Replica`, diğerleri → `Master` |
| 7 | Yukarıdakilerin hiçbiri | `options.DefaultTarget` |

6. kural **varsayılan olarak kapalıdır**. Bir `GET` her zaman okuma anlamına gelmez — çoğu
`LastSeenAt` damgalar, önbellek doldurur ya da denetim kaydı yazar — bu yüzden hepsini replica'ya
taşımak, bilinçli olarak kabul etmeniz gereken bir davranış değişikliğidir:

```csharp
options.RouteByHttpMethod = true;
```

Açtığınızda bilinmeyen metotlar master'a düşer: replica yönünde yanlış tahmin yazmaları bozar, master
yönünde yanlış tahmin yalnızca biraz kapasiteye mal olur.

---

## Bir sorgunun nereye gideceğini söylemek

**Attribute ile**, action veya controller bazında — action üzerindeki her zaman kazanır:

```csharp
[HttpGet("{id:int}")]
[UseMasterDb]                   // bayat veri okumaması gereken bir GET
public Task<Order?> Get(int id) => db.Orders.FindAsync(id).AsTask();

[HttpPost("search")]
[UseReplicaDb]                  // yalnızca okuyan bir POST
public Task<List<Order>> Search(SearchRequest r) { /* ... */ }
```

**Scope ile**, her yerde — `HttpContext`'in hiç olmadığı yerler dahil:

```csharp
using (dbTarget.UseReplicaDb())
{
    var ids = await db.Orders.Where(o => !o.Settled).Select(o => o.Id).ToListAsync();
}
```

**Minimal API yardımcılarıyla**, endpoint veya grup bazında:

```csharp
app.MapGet("/orders/{id:int}", handler).UseMasterDb();
var reports = app.MapGroup("/reports").UseReplicaDb();
```

> Bir Minimal API endpoint'i yalnızca bu yardımcılardan birini kullanırsanız ya da hatta
> `app.UseDbTargetRouting()` eklerseniz yönlendirilir. İkisi de yoksa `DefaultTarget` hedefine düşer.

---

## Çok replica ve biri düştüğünde

Topoloji **tek master ve istenildiği kadar replica**. Replica okumaları round-robin dağıtılır. Bir
replica bağlantıyı reddettiğinde paket hatayı yüzeye çıkarmaz: bağlantıyı bir sonraki replica'ya açar
ve ancak tüm replica'lar denendiğinde pes eder.

```csharp
options.MasterConnectionString  = "...";              // tam olarak bir tane
options.ReplicaConnectionString = "...replica-1...";
options.ReplicaConnectionStrings.Add("...replica-2...");
options.ReplicaConnectionStrings.Add("...replica-3...");
```

1. `IReplicaSelector` başlangıç replica'sını seçer — varsayılanı round-robin.
2. Biri bağlantıyı kabul edene kadar her replica sırayla aranır.
3. Reddeden replica `ReplicaFailureCooldown` süresince (varsayılan 30 sn) dinlendirilir; böylece ölü
   bir düğüm **her** isteğe bir bağlantı zaman aşımına mal olmaz. Sıranın sonuna alınır, asla
   yasaklanmaz — diğerleri de başarısız olursa yine denenir.
4. Hiçbir replica yanıt vermezse okumayı master karşılar; `AllowReplicaFallbackToMaster` kapalıysa
   bunun yerine `ReplicaUnavailableException` fırlatılır ve her sağlayıcı hatası bir
   `AggregateException` içinde taşınır.

Yazmalar asla failover edilmez, çünkü yazılacak tek bir master vardır.

### Failover'ın tek başına kapatamadığı durum

Bağlantı açmak, sunucuya erişmekle aynı şey değildir. ADO.NET bağlantıları havuzlar; bu yüzden bir
replica, havuz hâlâ ona ait sıcak tutamaçlar tutarken çökerse `OpenAsync` bunlardan birini **ağ turu
yapmadan geri verir ve başarı bildirir**. Soket ölüdür ama bunu ilk komut çalışana kadar kimse fark
etmez — o deneme için başka bir replica seçmek için çok geçtir.

* Bir replica'ya yönlendirilmiş bağlantıda komut hata verirse o replica anında düşmüş işaretlenir;
  böylece **sonraki** istek aynı havuzdan bir ölü tutamaç daha çekmek yerine o düğümden kaçınır.
* Başarısız komut burada yeniden denenmez — o, EF Core'un yürütme stratejisinin işidir.
* **O stratejiyi açın.** Yeniden deneme yeni bir bağlantı açar ve çoktan düşmüş işaretlenmiş
  düğümden kaçınarak yönlendirilir.

```csharp
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, cs) =>
    options.UseNpgsql(cs, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5)));
```

Retry açıkken bir replica kesintisi uygulama kodundan tamamen görünmez. Kapalıyken kesintiden sonraki
ilk istek bir bağlantı hatası verir, ardından trafik hayatta kalana oturur. İkisi de canlı testlerle
kapsanır.

`ReplicaUnavailableException` bilinçli olarak geçici (transient) değildir: tüm replica'lar arandı ve
reddetti demektir, biri geri gelene kadar yeniden denemek fayda etmez.

---

## Arka plan worker'ları, Hangfire, Quartz

`IDbTargetContext`, durumu bir `AsyncLocal` içinde yaşayan bir singleton'dır; bu yüzden web yığınının
dışında da birebir aynı şekilde çalışır:

```csharp
public sealed class SettlementWorker(
    IServiceScopeFactory scopeFactory,
    IDbTargetContext dbTarget) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        List<int> candidates;
        using (dbTarget.UseReplicaDb())          // ağır, gecikmeye dayanıklı tarama
        {
            candidates = await db.Orders.Where(o => !o.Settled).Select(o => o.Id).ToListAsync(stoppingToken);
        }

        using (dbTarget.UseMasterDb())           // otoriter güncelleme
        {
            /* ... */
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
```

Kayıt çağrısı web uygulamasındakiyle aynıdır — `AddEfCoreMasterReplica` bilinçli olarak hiçbir ASP.NET
Core tipine dokunmaz; bu yüzden runtime-only bir imajdaki `Microsoft.NET.Sdk.Worker` projesi, ASP.NET
Core paylaşılan çatısı kurulu olmadan derlenir ve çalışır.

---

## Dapper ve ham ADO.NET

Interceptor, **EF Core'un kendi açtığı** yollarda tetiklenir. Onun ötesine geçip bağlantı nesnesine
uzanmak bu kapsamın dışına çıkmaktır:

| Yazdığınız | Yönlendirilir mi? |
|---|---|
| `db.Database.OpenRoutedConnectionAsync(...)` | ✔ evet, failover'lı |
| `db.Database.OpenConnectionAsync()`, sonra `GetDbConnection()` üzerinde Dapper | ✔ evet, failover'lı |
| `db.Database.GetDbConnection()`, sonra kendiniz `Open()` | ✘ hayır |
| `db.Database.GetDbConnection()`, sonra Dapper (kendisi açar) | ✘ hayır |

**Keskin kenar.** Yönlendirilmemiş bir bağlantı nötr değildir: kendisine en son yazılan bağlantı
dizesini taşır. Replica'ya giden bir EF sorgusundan sonra EF bağlantıyı kapatır ama replica'nın
dizesini üzerinde bırakır; bu yüzden sonraki ham erişim o rotayı devralır — `UseMasterDb()` scope'u
içinde bile. Bu yolla yapılan bir Dapper `INSERT` salt okunur bir replica'ya ulaşır.

**Bunun yerine şunu yapın.** Hangi veritabanını istediğinizi söyleyin, açmayı EF Core yapsın:

```csharp
// Master.
await using var routed = await db.Database.OpenRoutedConnectionAsync(DbTarget.Master, cancellationToken);
var order = await routed.Connection.QuerySingleAsync<Order>("SELECT * FROM Orders WHERE Id = @id", new { id });

// Sağlıklı bir replica, aralarında failover ile.
await using var routed = await db.Database.OpenRoutedConnectionAsync(DbTarget.Replica, cancellationToken);
var rows = await routed.Connection.QueryAsync<Row>("SELECT ... /* ağır, gecikmeye dayanıklı */");

// Hiçbir şey söylemezseniz halihazırda geçerli olan hedefi alırsınız - varsa çevreleyen bir
// UseTarget scope'u, yoksa yapılandırdığınız DefaultTarget.
await using var routed = await db.Database.OpenRoutedConnectionAsync(cancellationToken);
```

Aynı iki biçimde senkron bir `OpenRoutedConnection()` de vardır. Tutamacı dispose etmek bağlantıyı
doğrudan kapatmak yerine context'e geri verir; çünkü EF Core bağlantıyı referans sayarak yönetir. İki
kez dispose etmek bir şey değiştirmez.

Hedef, yalnızca bağlantı açılırken bağlayıcıdır. Context zaten açık bir bağlantı tutuyorsa EF Core
mevcut olanı verir ve onun rotası geçerli kalır — bağlantı açıkken bağlantı dizesi değiştirilemez.

`DbContext`'ten almayıp kendiniz kurduğunuz bir bağlantı bu paketin tamamen dışındadır:
`IDbConnectionStringResolver`'ı çözüp dizeyi açıkça seçin.

---

## Kendi yazdığınızı okumak

İkisi de varsayılan olarak açık iki mekanizma:

* **`ForceMasterOnSaveChanges`** — `SaveChanges`, veritabanına dokunmadan *önce* master'a geçer;
  böylece `LastSeenAt` damgalayan ya da denetim kaydı yazan bir `GET` action'ı, salt okunur bir
  replica'da hata vermek yerine çalışır.
* **`StickToMasterAfterSaveChanges`** — başarılı bir kayıttan sonra scope'un geri kalanı master'da
  kalır; böylece yazmadan sonra okunan her şey master'dan okunur.

Geçerli scope'un ötesinde paket replikasyon gecikmesini tespit edemez; uygulama içinden hiçbir şey
edemez. Sonraki bir isteğin önceki bir yazmayı görmesi gerekiyorsa `UseMasterDb()` ile açıkça
sabitleyin.

---

## Açılış denetimleri

Paketi yanlış bağlamanın iki yolu eskiden hiçbir hata üretmiyordu. İkisi de artık ilk istekten önce
yakalanır.

**Interceptor'lar olmadan kaydedilmiş bir `DbContext` hata fırlatır.** Mesaj context'i adıyla söyler
ve iki çözümü de verir. Uyarı değil hatadır, çünkü paket o context için hiçbir şey yapmıyordur.
Bilinçli olarak başka yerde yaşayan ikinci bir context susturulmaz, beyan edilir:

```csharp
options.UnroutedDbContextTypes.Add(typeof(AuditDbContext));
```

**Yönlendirme mekanizması olmadan kaydedilmiş controller'lar uyarı verir.** Uygulama
`AddControllers()` çağırdıysa ama ne `services.AddDbTargetMvcFilter()` ne de
`app.UseDbTargetRouting()` çağrıldıysa, ikisini de adıyla anan bir uyarı loglanır. Hata değil uyarı,
çünkü yalnızca `UseTarget` scope'larıyla yönlendirme yapmak meşru bir tasarımdır.

Uygulamanın web uygulaması olup olmadığına, bir MVC tipine dokunarak değil servis tipi *adları*
eşleştirilerek karar verilir; böylece denetim, ASP.NET Core paylaşılan çatısı bulunmayan bir host'ta
da güvenlidir. İki denetim de bir `IHost` gerektirir.
`options.ValidateStartupWiring = false` ile kapatılır.

---

## DbContext kaydı

| Kayıt biçimi | Sonuç |
|---|---|
| `AddMasterReplicaDbContext<T>` | Yönlendirilir |
| `AddMasterReplicaDbContextPool<T>` | Yönlendirilir; havuzdaki örneklere rota durumu yapışmaz |
| `AddMasterReplicaDbContextFactory<T>` | Yönlendirilir; üretilen context onu *kullanan* akışı izler |
| `AddDbContext<T>` + `UseMasterReplicaSplitting(sp)` | Yönlendirilir, birebir aynı |
| Tek başına `AddDbContext<T>` | Yönlendirilmez — host başlamayı reddeder |

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(masterConnectionString);
    options.UseMasterReplicaSplitting(sp);
});
```

---

## Veritabanı sağlayıcıları

Paket hiçbir sağlayıcı tipini adıyla anmaz. Sağlayıcıya bakan yüzeyinin tamamı ADO.NET taban sınıfında
tanımlı dört üyedir — `DbConnection.State`, `DbConnection.ConnectionString`, `Open`/`OpenAsync` ve
`Close` — bu yüzden her EF Core ilişkisel sağlayıcısı çalışır.

| Sağlayıcı | Paket | Canlı sunucuda doğrulandı |
|---|---|---|
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | ✔ |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | ✔ |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | ✔ |
| Oracle | `Oracle.EntityFrameworkCore` | ✔ |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | ✔ |

Her biri üç gerçek veritabanı sunucusuna karşı çalışır — bir master ve iki replica, her birinde farklı
bir işaret satırı, böylece test hangisinin cevap verdiğini söyleyebilir. Entegrasyon paketi bunları
container'larda başlatır, ardından **test ortasında bir replica'nın container'ını durdurur** ve
okumaların hayatta kalan tarafından karşılanmaya devam ettiğini, tam kesintide master'a düşüldüğünü ve
toparlanan düğümün bekleme süresi dolunca yeniden kullanıldığını kanıtlar.

```bash
dotnet test tests/Appouse.EFCore.ReplicaManager.IntegrationTests   # Docker gerekir
```

Docker yoksa bu testler hata vermek yerine kendilerini atlar.

### Sağlayıcı notları

**MySQL — `ServerVersion.AutoDetect` kullanmayın.** Servis sağlayıcı kurulurken bağlantı açar; bu
yüzden açılış, henüz hiçbir yönlendirme yokken master'ın erişilebilir olmasına bağımlı hâle gelir.
Açıkça bir `MySqlServerVersion` verin.

**SQL Server — replica bağlantı dizelerini salt okunur işaretleyin.** Replica'lar Always On secondary
ise her replica dizesine `ApplicationIntent=ReadOnly` ekleyin. Master dizesinde bulunmamalıdır.

**PostgreSQL — Npgsql'in çok-host desteği tamamlayıcıdır.** `Host=h1,h2,h3` ve
`Target Session Attributes` ile bağlantıları kendisi dengeleyebilir, ama bir okumayı bir yazmadan bir
attribute veya scope'un ayırdığı gibi ayıramaz. İki biçimi de ya da ikisini birden kullanın.

**Oracle — Active Data Guard standby salt okunurdur** ve replica olarak çalışır. Oracle'ın EF Core
sağlayıcısının diğer üçünün aksine **`EnableRetryOnFailure` sunmadığını** unutmayın; sıcak havuz
kesintisinden şeffaf toparlanma için kendi yürütme stratejinizi yazmanız gerekir.
`tests/Appouse.EFCore.ReplicaManager.IntegrationTests/OracleCluster.cs` içinde kopyalanabilir bir
örnek var.

**SQLite'ta replikasyon yoktur.** Testler için kullanışlıdır — bu deponun kendi test paketi iki gerçek
SQLite dosyası arasında yönlendirme yapar — ama üretim topolojisi değildir.

**Her bağlantı dizesi kendi ADO.NET havuzunu alır.** Bir master artı üç replica dört havuz demektir,
her birinin kendi `Max Pool Size` değeriyle. Ayrıca model master bağlantı dizesinden kurulur, yani
replica'lar aynı şemayı sunmalıdır.

---

## Migration'lar

`MasterConnectionString` kanonik bağlantı dizesidir: sağlayıcının model oluşturmak için aldığı,
`dotnet ef` komutunun kullandığı ve `Database.Migrate()` çağrısının bağlandığı dizedir.
`DefaultTarget` değerini `Master` bırakın ya da migration'ı açıkça sarmalayın:

```csharp
using (dbTarget.UseMasterDb())
{
    await db.Database.MigrateAsync();
}
```

---

## Ayarlar

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `MasterConnectionString` | *(zorunlu)* | Tek master. |
| `ReplicaConnectionString` | *(zorunlu)* | İlk replica. Ayrımı kapatmak için master ile aynı değeri verin. |
| `ReplicaConnectionStrings` | boş | Ek replica'lar; yük dağıtılır ve failover uygulanır. |
| `DefaultTarget` | `Master` | Hiçbir kural geçerli olmadığında kullanılır. |
| `RouteByHttpMethod` | **`false`** | HTTP metodu konvansiyonunu uygular. Kapalıdır; paketi eklemek hiçbir trafiği taşımaz. |
| `ForceMasterInsideTransaction` | `true` | Transaction'lar her zaman master kullanır. |
| `ForceMasterOnSaveChanges` | `true` | `SaveChanges` her zaman master kullanır. |
| `StickToMasterAfterSaveChanges` | `true` | Scope içinde yazma sonrası okuma tutarlılığı. |
| `AllowReplicaFallbackToMaster` | `true` | Hiçbir replica yanıt vermezse master'ı kullanır. |
| `ReplicaFailureCooldown` | `30 sn` | Başarısız bir replica'nın ne kadar dinlendirileceği. |
| `ValidateStartupWiring` | `true` | Hiç bağlanmamış bir context veya web uygulaması için erken hata. |
| `UnroutedDbContextTypes` | boş | Bilinçli olarak yönlendirilmemiş context'ler. |
| `MvcActionFilterOrder` | `int.MinValue` | Filtre konumu; en düşük önce çalışır. |

Dilerseniz yapılandırmadan bağlayın:

```csharp
builder.Services.AddEfCoreMasterReplica(
    builder.Configuration.GetSection(MasterReplicaOptions.SectionName));   // "EfCoreMasterReplica"
```

Yapılandırma host açılışında doğrulanır; eksik bir bağlantı dizesi ilk sorguda hata vermek yerine
uygulamayı hiç başlatmaz.

---

## Genişletme noktaları

```csharp
// Round-robin yerine gecikme, sağlık veya konuma göre replica seçin.
services.Replace(ServiceDescriptor.Singleton<IReplicaSelector, MyReplicaSelector>());

// Replica erişilebilirliğini kendi yönteminizle izleyin.
services.Replace(ServiceDescriptor.Singleton<IReplicaHealthMonitor, MyHealthMonitor>());

// Bağlantı dizelerini kiracı kataloğundan veya bir sır deposundan alın.
services.Replace(ServiceDescriptor.Singleton<IDbConnectionStringResolver, MyResolver>());
```

---

## Nasıl çalışır

Singleton bir `DbConnectionInterceptor`, EF Core bağlantıyı açmadan hemen önce
`ConnectionOpening` / `ConnectionOpeningAsync` içinde `DbConnection.ConnectionString` değerini yeniden
yazar. Replica okumalarında EF Core'un kendi açma işlemini bastırıp bağlantıyı kendisi açar; tek bir
işlem içinde farklı bir bağlantı dizesini yeniden denemenin tek yolu budur ve failover'ı mümkün kılan
şey de budur.

Üç tasarım kararı taşıyıcıdır:

* **Her şey singleton.** Interceptor'lar `DbContextOptions` içinde tutulur ve EF Core iç servis
  sağlayıcı önbelleğini bu ayarlara göre anahtarlar. Scope başına bir interceptor, EF'in her istekte
  yeni bir iç sağlayıcı kurmasına yol açardı — sınırsız bellek büyümesi ve EF'in *"More than twenty
  IServiceProvider instances have been created"* uyarısı. İstek bazlı durumun tamamı bunun yerine bir
  `AsyncLocal` içinde yaşar; `HttpContext` gerekmemesinin sebebi de budur.
* **Ortam değeri çıplak bir değer değil, değiştirilebilir bir kaptır.** `AsyncLocal<T>.Value`'ya
  atama, çağrı yığınında yukarıdaki çağıranlara görünmez; bu yüzden `SaveChangesAsync`'in
  derinliğinde fark edilen bir yazma, isteğin geri kalanını master'a asla sabitleyemezdi. Küçük,
  değiştirilebilir bir nesne tutup alanını değiştirmek bunu çözer; `UseTarget` ise *yeni* bir kap
  kurar, böylece etkisi akışa özel kalır ve `Dispose` ile geri alınır.
* **Web olmayan bir host'un çağırdığı hiçbir metotta MVC tipi geçmez.** CLR, bir metodu
  çalıştırmadan önce JIT ederken tip referanslarını çözer; bu yüzden `AddEfCoreMasterReplica` içinde
  tek bir `MvcOptions` geçmesi, runtime-only bir imajdaki Worker Service'i çökertirdi — etrafına
  `try`/`catch` koymak da kurtarmazdı. MVC bağlantısı `AddDbTargetMvcFilter` içindedir ve paketin
  `FrameworkReference` tanımı `PrivateAssets="all"` taşır; böylece ASP.NET Core paylaşılan çatısı
  hiçbir tüketicinin çalışma zamanı gereksinimi hâline gelmez.

### Sınırlar

* Bağlantı dizesi yalnızca bağlantı **kapalıyken** atanabilir. EF Core geç açıp erken kapattığı için
  her işlem bağımsız yönlendirilir — ancak açık bir transaction sırasında veya
  `Database.OpenConnection()` sonrasında değil. Bu tür işleri bir `UseTarget(...)` scope'u içinde
  başlatın; rota, bağlantının açıldığı anda sabitlenir.
* Ertelenmiş sorgular, kurulduğu andaki değil *çalıştığı* andaki hedefi izler.
* Bağlantı dizeleri hiçbir seviyede loglanmaz.

---

## Lisans

MIT. Bkz. [LICENSE](https://github.com/appouse/Appouse.EFCore.ReplicaManager/blob/main/LICENSE).
