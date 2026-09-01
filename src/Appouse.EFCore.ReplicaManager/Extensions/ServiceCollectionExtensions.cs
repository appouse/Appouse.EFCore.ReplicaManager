using System;
using Appouse.EFCore.ReplicaManager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration entry points for master/replica database splitting.
/// <para>TR: Master/replica veritabanı ayrımı için kayıt giriş noktaları.</para>
/// </summary>
/// <remarks>
/// <para>
/// Declared in the <c>Microsoft.Extensions.DependencyInjection</c> namespace on purpose, so
/// <c>builder.Services.AddEfCoreMasterReplica(...)</c> is offered by IntelliSense in
/// <c>Program.cs</c> without an extra <c>using</c>.
/// </para>
/// <para>
/// TR: <c>builder.Services.AddEfCoreMasterReplica(...)</c> çağrısının <c>Program.cs</c> içinde
/// ek bir <c>using</c> olmadan IntelliSense'te görünmesi için bilinçli olarak
/// <c>Microsoft.Extensions.DependencyInjection</c> ad alanında tanımlanmıştır.
/// </para>
/// <para>
/// <strong>Nothing in this class touches an ASP.NET Core MVC type</strong>, and that is a hard
/// constraint rather than a stylistic one. The CLR resolves assembly references when it JIT-compiles a
/// method, before executing its first instruction, so a single mention of <c>MvcOptions</c> here -
/// even inside a branch that never runs, or inside a <c>try</c> block - would make this method throw
/// <see cref="System.IO.FileNotFoundException"/> in a Worker Service running on a runtime-only image
/// that has no ASP.NET Core shared framework. MVC wiring therefore lives in its own extension method
/// that only web applications call; see <c>AddDbTargetMvcFilter</c>.
/// </para>
/// <para>
/// TR: <strong>Bu sınıfın hiçbir yeri bir ASP.NET Core MVC tipine dokunmaz</strong> ve bu bir üslup
/// tercihi değil, katı bir kısıttır. CLR, bir metodu JIT ile derlerken ilk komutunu çalıştırmadan önce
/// assembly referanslarını çözer; dolayısıyla burada tek bir <c>MvcOptions</c> geçmesi - hiç
/// çalışmayan bir dalın içinde veya bir <c>try</c> bloğunda olsa bile - ASP.NET Core paylaşılan
/// çatısının bulunmadığı bir imajda çalışan Worker Service'te bu metodun
/// <see cref="System.IO.FileNotFoundException"/> fırlatmasına yol açar. Bu yüzden MVC bağlantısı
/// yalnızca web uygulamalarının çağırdığı kendi uzantı metodundadır; bkz. <c>AddDbTargetMvcFilter</c>.
/// </para>
/// </remarks>
public static class ReplicaManagerServiceCollectionExtensions
{
    /// <summary>
    /// Registers master/replica splitting with inline configuration.
    /// <para>TR: Master/replica ayrımını satır içi yapılandırmayla kaydeder.</para>
    /// </summary>
    /// <param name="services">
    /// The service collection.
    /// <para>TR: Servis koleksiyonu.</para>
    /// </param>
    /// <param name="configure">
    /// Callback that populates <see cref="MasterReplicaOptions"/>.
    /// <para>TR: <see cref="MasterReplicaOptions"/> değerlerini dolduran geri çağrı.</para>
    /// </param>
    /// <param name="validateOnStart">
    /// When <see langword="true"/> (the default), the configuration is validated during host start-up,
    /// so a bad connection string stops the host instead of surfacing on the first query.
    /// <para>
    /// TR: <see langword="true"/> ise (varsayılan), yapılandırma host başlarken doğrulanır; böylece
    /// hatalı bir bağlantı dizesi ilk sorguda ortaya çıkmak yerine host'u hiç başlatmaz.
    /// </para>
    /// </param>
    /// <returns>
    /// The same <paramref name="services"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="services"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any required argument is <see langword="null"/>.
    /// <para>TR: Zorunlu argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddEfCoreMasterReplica(options =&gt;
    /// {
    ///     options.MasterConnectionString = builder.Configuration.GetConnectionString("Master")!;
    ///     options.ReplicaConnectionString = builder.Configuration.GetConnectionString("Replica1")!;
    ///     options.ReplicaConnectionStrings.Add(builder.Configuration.GetConnectionString("Replica2")!);
    ///     options.DefaultTarget = DbTarget.Master;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddEfCoreMasterReplica(
        this IServiceCollection services,
        Action<MasterReplicaOptions> configure,
        bool validateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = services.AddOptions<MasterReplicaOptions>().Configure(configure);
        return AddCore(services, builder, validateOnStart);
    }

    /// <summary>
    /// Registers master/replica splitting, binding <see cref="MasterReplicaOptions"/> from
    /// configuration.
    /// <para>
    /// TR: Master/replica ayrımını, <see cref="MasterReplicaOptions"/> değerlerini yapılandırmadan
    /// bağlayarak kaydeder.
    /// </para>
    /// </summary>
    /// <param name="services">
    /// The service collection.
    /// <para>TR: Servis koleksiyonu.</para>
    /// </param>
    /// <param name="configuration">
    /// The configuration section to bind, typically
    /// <c>builder.Configuration.GetSection(MasterReplicaOptions.SectionName)</c>.
    /// <para>
    /// TR: Bağlanacak yapılandırma bölümü; genellikle
    /// <c>builder.Configuration.GetSection(MasterReplicaOptions.SectionName)</c>.
    /// </para>
    /// </param>
    /// <param name="configure">
    /// Optional callback applied after binding, to override or complete values.
    /// <para>
    /// TR: Bağlamadan sonra uygulanan, değerleri geçersiz kılmak veya tamamlamak için isteğe bağlı
    /// geri çağrı.
    /// </para>
    /// </param>
    /// <param name="validateOnStart">
    /// When <see langword="true"/> (the default), the configuration is validated during host start-up.
    /// <para>TR: <see langword="true"/> ise (varsayılan), yapılandırma host başlarken doğrulanır.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="services"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="services"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any required argument is <see langword="null"/>.
    /// <para>TR: Zorunlu argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    /// <example>
    /// <c>appsettings.json</c>:
    /// <code>
    /// {
    ///   "EfCoreMasterReplica": {
    ///     "MasterConnectionString": "Host=master;Database=app",
    ///     "ReplicaConnectionString": "Host=replica-1;Database=app",
    ///     "ReplicaConnectionStrings": [ "Host=replica-2;Database=app" ],
    ///     "DefaultTarget": "Replica"
    ///   }
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddEfCoreMasterReplica(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MasterReplicaOptions>? configure = null,
        bool validateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services.AddOptions<MasterReplicaOptions>().Bind(configuration);
        if (configure is not null)
        {
            builder.Configure(configure);
        }

        return AddCore(services, builder, validateOnStart);
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> with master/replica splitting already wired in.
    /// <para>
    /// TR: <typeparamref name="TContext"/> türünü, master/replica ayrımı bağlanmış hâlde kaydeder.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">
    /// The context type to register.
    /// <para>TR: Kaydedilecek context türü.</para>
    /// </typeparam>
    /// <param name="services">
    /// The service collection.
    /// <para>TR: Servis koleksiyonu.</para>
    /// </param>
    /// <param name="configureProvider">
    /// Supplies the provider call. The package stays provider-agnostic by handing you the connection
    /// string and letting you choose the provider:
    /// <c>(options, connectionString) =&gt; options.UseNpgsql(connectionString)</c>.
    /// <para>
    /// TR: Sağlayıcı çağrısını verir. Paket, bağlantı dizesini size verip sağlayıcı seçimini size
    /// bırakarak sağlayıcıdan bağımsız kalır:
    /// <c>(options, connectionString) =&gt; options.UseNpgsql(connectionString)</c>.
    /// </para>
    /// </param>
    /// <param name="contextLifetime">
    /// Lifetime of <typeparamref name="TContext"/>. Defaults to scoped.
    /// <para>TR: <typeparamref name="TContext"/> yaşam süresi. Varsayılanı scoped.</para>
    /// </param>
    /// <param name="optionsLifetime">
    /// Lifetime of its options. Defaults to scoped.
    /// <para>TR: Ayarlarının yaşam süresi. Varsayılanı scoped.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="services"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="services"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any required argument is <see langword="null"/>.
    /// <para>TR: Zorunlu argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    /// <remarks>
    /// The connection string handed to <paramref name="configureProvider"/> is
    /// <see cref="MasterReplicaOptions.MasterConnectionString"/>. That is the canonical one: it is what
    /// the provider uses for model building, what <c>dotnet ef</c> and <c>Database.Migrate()</c>
    /// connect to, and the safe value to fall back on. The interceptor rewrites it per connection at
    /// open time, so a replica is used whenever the ambient target says so.
    /// <para>
    /// TR: <paramref name="configureProvider"/> parametresine verilen bağlantı dizesi
    /// <see cref="MasterReplicaOptions.MasterConnectionString"/> değeridir. Kanonik olan budur:
    /// sağlayıcının model oluşturmak için kullandığı, <c>dotnet ef</c> ve <c>Database.Migrate()</c>
    /// çağrılarının bağlandığı ve geri düşülecek güvenli değerdir. Interceptor bunu her bağlantı için
    /// açılış anında yeniden yazar; böylece ortam hedefi öyle diyorsa replica kullanılır.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddMasterReplicaDbContext&lt;AppDbContext&gt;((options, connectionString) =&gt;
    ///     options.UseSqlServer(connectionString));
    /// </code>
    /// </example>
    public static IServiceCollection AddMasterReplicaDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder, string> configureProvider,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddDbContext<TContext>(
            (serviceProvider, options) => ConfigureContext(serviceProvider, options, configureProvider),
            contextLifetime,
            optionsLifetime);

        return services;
    }

    /// <summary>
    /// Pooled counterpart of
    /// <see cref="AddMasterReplicaDbContext{TContext}(IServiceCollection,Action{DbContextOptionsBuilder,string},ServiceLifetime,ServiceLifetime)"/>.
    /// <para>
    /// TR:
    /// <see cref="AddMasterReplicaDbContext{TContext}(IServiceCollection,Action{DbContextOptionsBuilder,string},ServiceLifetime,ServiceLifetime)"/>
    /// metodunun havuzlanmış karşılığı.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">
    /// The context type to register.
    /// <para>TR: Kaydedilecek context türü.</para>
    /// </typeparam>
    /// <param name="services">
    /// The service collection.
    /// <para>TR: Servis koleksiyonu.</para>
    /// </param>
    /// <param name="configureProvider">
    /// Supplies the provider call, given the master connection string.
    /// <para>TR: Master bağlantı dizesiyle sağlayıcı çağrısını verir.</para>
    /// </param>
    /// <param name="poolSize">
    /// Maximum number of pooled instances. Defaults to EF Core's own default of 1024.
    /// <para>
    /// TR: Havuzdaki en fazla örnek sayısı. Varsayılanı EF Core'un kendi varsayılanı olan 1024.
    /// </para>
    /// </param>
    /// <returns>
    /// The same <paramref name="services"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="services"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any required argument is <see langword="null"/>.
    /// <para>TR: Zorunlu argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    /// <remarks>
    /// Pooling is safe here because the interceptors are stateless singletons and the routing decision
    /// is re-evaluated every time a connection is opened, never cached on the context.
    /// <para>
    /// TR: Havuzlama burada güvenlidir; çünkü interceptor'lar durumsuz singleton'lardır ve yönlendirme
    /// kararı her bağlantı açılışında yeniden verilir, context üzerinde asla önbelleklenmez.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMasterReplicaDbContextPool<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder, string> configureProvider,
        int poolSize = 1024)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddDbContextPool<TContext>(
            (serviceProvider, options) => ConfigureContext(serviceProvider, options, configureProvider),
            poolSize);

        return services;
    }

    /// <summary>
    /// Registers an <see cref="IDbContextFactory{TContext}"/> for <typeparamref name="TContext"/> with
    /// master/replica splitting already wired in.
    /// <para>
    /// TR: <typeparamref name="TContext"/> için master/replica ayrımı bağlanmış bir
    /// <see cref="IDbContextFactory{TContext}"/> kaydeder.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">
    /// The context type to produce.
    /// <para>TR: Üretilecek context türü.</para>
    /// </typeparam>
    /// <param name="services">
    /// The service collection.
    /// <para>TR: Servis koleksiyonu.</para>
    /// </param>
    /// <param name="configureProvider">
    /// Supplies the provider call, given the master connection string.
    /// <para>TR: Master bağlantı dizesiyle sağlayıcı çağrısını verir.</para>
    /// </param>
    /// <param name="lifetime">
    /// Lifetime of the factory. Defaults to singleton, matching EF Core.
    /// <para>TR: Fabrikanın yaşam süresi. EF Core ile aynı şekilde varsayılanı singleton.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="services"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="services"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any required argument is <see langword="null"/>.
    /// <para>TR: Zorunlu argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    /// <remarks>
    /// A context produced by the factory follows the target of the flow that <em>uses</em> it, not the
    /// one that created it, because routing happens when the connection opens rather than when the
    /// context is constructed.
    /// <para>
    /// TR: Fabrikadan alınan bir context, kendisini <em>kullanan</em> akışın hedefini izler, onu
    /// oluşturan akışınkini değil; çünkü yönlendirme context kurulurken değil, bağlantı açılırken
    /// gerçekleşir.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMasterReplicaDbContextFactory<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder, string> configureProvider,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddDbContextFactory<TContext>(
            (serviceProvider, options) => ConfigureContext(serviceProvider, options, configureProvider),
            lifetime);

        return services;
    }

    private static void ConfigureContext(
        IServiceProvider serviceProvider,
        DbContextOptionsBuilder options,
        Action<DbContextOptionsBuilder, string> configureProvider)
    {
        var masterReplicaOptions = serviceProvider.GetRequiredService<IOptions<MasterReplicaOptions>>().Value;
        configureProvider(options, masterReplicaOptions.MasterConnectionString);
        options.UseMasterReplicaSplitting(serviceProvider);
    }

    private static IServiceCollection AddCore(
        IServiceCollection services,
        OptionsBuilder<MasterReplicaOptions> optionsBuilder,
        bool validateOnStart)
    {
        services.AddLogging();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MasterReplicaOptions>, MasterReplicaOptionsValidator>());

        if (validateOnStart)
        {
            optionsBuilder.ValidateOnStart();
        }

        // Every one of these is a singleton, and that is load-bearing. Interceptors are captured
        // inside DbContextOptions, and EF Core keys its internal service-provider cache on those
        // options: a per-scope interceptor instance would make EF build a fresh internal provider per
        // scope, leaking memory and eventually logging "More than twenty IServiceProvider instances
        // have been created". All per-request state lives in the AsyncLocal inside DbTargetContext
        // instead. The health monitor must also be shared, or a replica outage would be re-discovered
        // by every scope.
        services.TryAddSingleton<IReplicaSelector, RoundRobinReplicaSelector>();
        services.TryAddSingleton<IDbConnectionStringResolver, DbConnectionStringResolver>();
        services.TryAddSingleton<IReplicaHealthMonitor, ReplicaHealthMonitor>();
        services.TryAddSingleton<ConnectionRouteRegistry>();

        // Registered through an explicit factory because DbTargetContext offers two single-argument
        // constructors, and the container must not have to guess between them.
        services.TryAddSingleton<IDbTargetContext>(serviceProvider =>
            new DbTargetContext(serviceProvider.GetRequiredService<IOptions<MasterReplicaOptions>>()));

        services.TryAddSingleton<MasterReplicaDbInterceptor>();
        services.TryAddSingleton<MasterStickinessSaveChangesInterceptor>();
        services.TryAddSingleton<ReplicaCommandFailureInterceptor>();

        return services;
    }
}
