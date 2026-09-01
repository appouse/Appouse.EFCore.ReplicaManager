using System;
using Appouse.EFCore.ReplicaManager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration entry points for read/write (master/replica) splitting.
/// </summary>
/// <remarks>
/// <para>
/// Declared in the <c>Microsoft.Extensions.DependencyInjection</c> namespace on purpose, so that
/// <c>builder.Services.AddEfCoreReadWriteSplit(...)</c> is offered by IntelliSense in
/// <c>Program.cs</c> without an extra <c>using</c>.
/// </para>
/// <para>
/// <strong>Nothing in this class touches an ASP.NET Core MVC type</strong>, and that is a hard
/// constraint rather than a stylistic one. The CLR resolves assembly references when it JIT-compiles
/// a method, before executing its first instruction, so a single mention of <c>MvcOptions</c> here -
/// even inside a branch that never runs, or inside a <c>try</c> block - would make this method throw
/// <see cref="System.IO.FileNotFoundException"/> in a Worker Service running on a runtime-only image
/// that has no ASP.NET Core shared framework. MVC wiring therefore lives in its own extension method
/// that only web applications call; see <c>AddDbTargetMvcFilter</c>.
/// </para>
/// </remarks>
public static class ReplicaManagerServiceCollectionExtensions
{
    /// <summary>
    /// Registers read/write splitting with inline configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback that populates <see cref="ReadWriteOptions"/>.</param>
    /// <param name="validateOnStart">
    /// When <see langword="true"/> (the default), the configuration is validated during host
    /// start-up, so a bad connection string stops the host instead of surfacing on the first query.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddEfCoreReadWriteSplit(options =&gt;
    /// {
    ///     options.WriteConnectionString = builder.Configuration.GetConnectionString("Master")!;
    ///     options.ReadConnectionString = builder.Configuration.GetConnectionString("Replica")!;
    ///     options.DefaultTarget = DbTarget.ReadReplica;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddEfCoreReadWriteSplit(
        this IServiceCollection services,
        Action<ReadWriteOptions> configure,
        bool validateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = services.AddOptions<ReadWriteOptions>().Configure(configure);
        return AddCore(services, builder, validateOnStart);
    }

    /// <summary>
    /// Registers read/write splitting, binding <see cref="ReadWriteOptions"/> from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// The configuration section to bind, typically
    /// <c>builder.Configuration.GetSection(ReadWriteOptions.SectionName)</c>.
    /// </param>
    /// <param name="configure">Optional callback applied after binding, to override or complete values.</param>
    /// <param name="validateOnStart">
    /// When <see langword="true"/> (the default), the configuration is validated during host start-up.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    /// <example>
    /// <c>appsettings.json</c>:
    /// <code>
    /// {
    ///   "EfCoreReadWriteSplit": {
    ///     "WriteConnectionString": "Host=master;Database=app",
    ///     "ReadConnectionString": "Host=replica-1;Database=app",
    ///     "ReadConnectionStrings": [ "Host=replica-2;Database=app" ],
    ///     "DefaultTarget": "ReadReplica"
    ///   }
    /// }
    /// </code>
    /// <code>
    /// builder.Services.AddEfCoreReadWriteSplit(
    ///     builder.Configuration.GetSection(ReadWriteOptions.SectionName));
    /// </code>
    /// </example>
    public static IServiceCollection AddEfCoreReadWriteSplit(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ReadWriteOptions>? configure = null,
        bool validateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services.AddOptions<ReadWriteOptions>().Bind(configuration);
        if (configure is not null)
        {
            builder.Configure(configure);
        }

        return AddCore(services, builder, validateOnStart);
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> with read/write splitting already wired in.
    /// </summary>
    /// <typeparam name="TContext">The context type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureProvider">
    /// Supplies the provider call. The package stays provider-agnostic by handing you the
    /// connection string and letting you choose the provider:
    /// <c>(options, connectionString) =&gt; options.UseNpgsql(connectionString)</c>.
    /// </param>
    /// <param name="contextLifetime">Lifetime of <typeparamref name="TContext"/>. Defaults to scoped.</param>
    /// <param name="optionsLifetime">Lifetime of its options. Defaults to scoped.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The connection string handed to <paramref name="configureProvider"/> is
    /// <see cref="ReadWriteOptions.WriteConnectionString"/>. That is the canonical one: it is what
    /// the provider uses for model building, what <c>dotnet ef</c> and <c>Database.Migrate()</c>
    /// connect to, and the safe value to fall back on. The interceptor rewrites it per connection
    /// at open time, so a replica is used whenever the ambient target says so.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddReadWriteDbContext&lt;AppDbContext&gt;((options, connectionString) =&gt;
    ///     options.UseSqlServer(connectionString));
    /// </code>
    /// </example>
    public static IServiceCollection AddReadWriteDbContext<TContext>(
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
    /// <see cref="AddReadWriteDbContext{TContext}(IServiceCollection,Action{DbContextOptionsBuilder,string},ServiceLifetime,ServiceLifetime)"/>.
    /// </summary>
    /// <typeparam name="TContext">The context type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureProvider">Supplies the provider call, given the master connection string.</param>
    /// <param name="poolSize">Maximum number of pooled instances. Defaults to EF Core's own default of 1024.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Pooling is safe here because the interceptors are stateless singletons and the routing
    /// decision is re-evaluated every time a connection is opened, never cached on the context.
    /// </remarks>
    public static IServiceCollection AddReadWriteDbContextPool<TContext>(
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

    private static void ConfigureContext(
        IServiceProvider serviceProvider,
        DbContextOptionsBuilder options,
        Action<DbContextOptionsBuilder, string> configureProvider)
    {
        var readWriteOptions = serviceProvider.GetRequiredService<IOptions<ReadWriteOptions>>().Value;
        configureProvider(options, readWriteOptions.WriteConnectionString);
        options.UseReadWriteSplitting(serviceProvider);
    }

    private static IServiceCollection AddCore(
        IServiceCollection services,
        OptionsBuilder<ReadWriteOptions> optionsBuilder,
        bool validateOnStart)
    {
        services.AddLogging();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ReadWriteOptions>, ReadWriteOptionsValidator>());

        if (validateOnStart)
        {
            optionsBuilder.ValidateOnStart();
        }

        // Every one of these is a singleton, and that is load-bearing. Interceptors are captured
        // inside DbContextOptions, and EF Core keys its internal service-provider cache on those
        // options: a per-scope interceptor instance would make EF build a fresh internal provider
        // per scope, leaking memory and eventually logging "More than twenty IServiceProvider
        // instances have been created". All per-request state lives in the AsyncLocal inside
        // DbTargetContext instead.
        services.TryAddSingleton<IReadReplicaSelector, RoundRobinReadReplicaSelector>();
        services.TryAddSingleton<IDbConnectionStringResolver, DbConnectionStringResolver>();

        // Registered through an explicit factory because DbTargetContext offers two single-argument
        // constructors, and the container must not have to guess between them.
        services.TryAddSingleton<IDbTargetContext>(serviceProvider =>
            new DbTargetContext(serviceProvider.GetRequiredService<IOptions<ReadWriteOptions>>()));

        services.TryAddSingleton<ReadWriteDbInterceptor>();
        services.TryAddSingleton<WriteStickinessSaveChangesInterceptor>();

        return services;
    }
}
