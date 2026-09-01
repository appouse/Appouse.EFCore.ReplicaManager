using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager.Internal;

/// <summary>
/// Turns the package's two silent misconfigurations into loud ones at start-up.
/// <para>TR: Paketin iki sessiz yapılandırma hatasını açılışta gürültülü hâle getirir.</para>
/// </summary>
/// <remarks>
/// <para>
/// Both failures share the same shape: everything compiles, nothing throws, and the application
/// quietly sends every query to <see cref="MasterReplicaOptions.DefaultTarget"/> while looking
/// correctly configured. Registering a <see cref="DbContext"/> without the interceptors is a hard
/// error - the package is doing literally nothing for that context. Registering controllers without
/// any routing mechanism is a warning, because an application may legitimately route only through
/// explicit <see cref="IDbTargetContext.UseTarget"/> scopes.
/// </para>
/// <para>
/// TR: İki hata da aynı biçimde: her şey derlenir, hiçbir şey fırlatmaz ve uygulama doğru
/// yapılandırılmış görünürken tüm sorguları sessizce
/// <see cref="MasterReplicaOptions.DefaultTarget"/> hedefine gönderir. Bir <see cref="DbContext"/>
/// türünü interceptor'lar olmadan kaydetmek kesin hatadır - paket o context için hiçbir şey
/// yapmıyordur. Controller'ları hiçbir yönlendirme mekanizması olmadan kaydetmek ise uyarıdır;
/// çünkü bir uygulama meşru olarak yalnızca açık <see cref="IDbTargetContext.UseTarget"/> scope'ları
/// üzerinden yönlendirme yapıyor olabilir.
/// </para>
/// <para>
/// Whether the application is a web application is decided by matching service type <em>names</em>
/// rather than by touching an MVC type, so this class stays safe to run on a host with no ASP.NET
/// Core shared framework.
/// </para>
/// <para>
/// TR: Uygulamanın bir web uygulaması olup olmadığına, bir MVC tipine dokunarak değil servis tipi
/// <em>adlarını</em> eşleştirerek karar verilir; böylece bu sınıf ASP.NET Core paylaşılan çatısı
/// bulunmayan bir host'ta da güvenle çalışır.
/// </para>
/// </remarks>
internal sealed class StartupWiringValidator : IHostedService
{
    private const string MvcServicePrefix = "Microsoft.AspNetCore.Mvc.";

    private readonly ILogger<StartupWiringValidator> _logger;
    private readonly DbTargetRoutingMarker _marker;
    private readonly MasterReplicaOptions _options;
    private readonly IServiceCollection _services;
    private readonly IServiceProvider _serviceProvider;

    internal StartupWiringValidator(
        IServiceCollection services,
        IServiceProvider serviceProvider,
        DbTargetRoutingMarker marker,
        IOptions<MasterReplicaOptions> options,
        ILogger<StartupWiringValidator> logger)
    {
        _services = services;
        _serviceProvider = serviceProvider;
        _marker = marker;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ValidateStartupWiring)
        {
            return Task.CompletedTask;
        }

        ValidateDbContexts();
        WarnIfWebApplicationHasNoRouting();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Every registered <see cref="DbContext"/> must carry the routing interceptor, or the package
    /// silently does nothing for it.
    /// </summary>
    private void ValidateDbContexts()
    {
        var unrouted = new List<string>();

        using var scope = _serviceProvider.CreateScope();

        foreach (var optionsType in RegisteredDbContextOptionsTypes())
        {
            var contextType = optionsType.GetGenericArguments()[0];
            if (_options.UnroutedDbContextTypes.Contains(contextType))
            {
                continue;
            }

            if (scope.ServiceProvider.GetService(optionsType) is not DbContextOptions dbContextOptions)
            {
                continue;
            }

            var interceptors = dbContextOptions.FindExtension<CoreOptionsExtension>()?.Interceptors;
            if (interceptors?.OfType<MasterReplicaDbInterceptor>().Any() != true)
            {
                unrouted.Add(contextType.Name);
            }
        }

        if (unrouted.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Master/replica splitting is registered, but {string.Join(", ", unrouted)} " +
            $"{(unrouted.Count == 1 ? "was" : "were")} registered without it, so every query from " +
            $"{(unrouted.Count == 1 ? "that context" : "those contexts")} ignores the ambient target and goes to " +
            $"whichever connection string the provider was given. Register the context with " +
            $"services.AddMasterReplicaDbContext<TContext>((options, connectionString) => ...), or, if you call " +
            $"AddDbContext yourself, add options.UseMasterReplicaSplitting(serviceProvider) inside it. " +
            $"To leave a context deliberately unrouted, add its type to " +
            $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.UnroutedDbContextTypes)}, or turn this " +
            $"check off with {nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.ValidateStartupWiring)}.");
    }

    /// <summary>
    /// An application that registered controllers but no routing mechanism gets a warning rather
    /// than an exception, because routing purely through UseTarget scopes is a legitimate choice.
    /// </summary>
    private void WarnIfWebApplicationHasNoRouting()
    {
        if (_marker.MvcFilterRegistered || _marker.MiddlewareRegistered)
        {
            return;
        }

        var hasMvc = _services.Any(descriptor =>
            descriptor.ServiceType.FullName?.StartsWith(MvcServicePrefix, StringComparison.Ordinal) == true);

        if (hasMvc)
        {
            Log.MvcRoutingNotWired(_logger);
        }
    }

    private IEnumerable<Type> RegisteredDbContextOptionsTypes()
        => _services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .Distinct();
}
