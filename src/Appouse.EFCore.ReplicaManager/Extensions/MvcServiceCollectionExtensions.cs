using System;
using Appouse.EFCore.ReplicaManager;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Opt-in MVC and Razor Pages integration.
/// <para>TR: İsteğe bağlı MVC ve Razor Pages entegrasyonu.</para>
/// </summary>
/// <remarks>
/// Kept apart from
/// <see cref="ReplicaManagerServiceCollectionExtensions.AddEfCoreMasterReplica(IServiceCollection,Action{MasterReplicaOptions},bool)"/>
/// so non-web hosts never JIT a method that references an MVC type. Call these only from an
/// application that has the ASP.NET Core shared framework - which any application calling
/// <c>AddControllers()</c> already does.
/// <para>
/// TR: Web dışı host'ların bir MVC tipine referans veren metodu asla JIT etmemesi için
/// <see cref="ReplicaManagerServiceCollectionExtensions.AddEfCoreMasterReplica(IServiceCollection,Action{MasterReplicaOptions},bool)"/>
/// metodundan ayrı tutulur. Bunları yalnızca ASP.NET Core paylaşılan çatısına sahip bir uygulamadan
/// çağırın; <c>AddControllers()</c> çağıran her uygulama zaten buna sahiptir.
/// </para>
/// </remarks>
public static class ReplicaManagerMvcServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="DbTargetActionFilter"/> to the global MVC filter collection, so every controller
    /// action and Razor Page handler is routed by attribute or HTTP verb.
    /// <para>
    /// TR: <see cref="DbTargetActionFilter"/> filtresini global MVC filtre koleksiyonuna ekler; böylece
    /// her controller action'ı ve Razor Page handler'ı attribute'a veya HTTP metoduna göre yönlendirilir.
    /// </para>
    /// </summary>
    /// <param name="services">
    /// The service collection.
    /// <para>TR: Servis koleksiyonu.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="services"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="services"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="services"/> <see langword="null"/>.</para>
    /// </exception>
    /// <remarks>
    /// Order does not matter relative to <c>AddControllers()</c>: this configures
    /// <see cref="MvcOptions"/> through the options pipeline, which MVC reads when it is built. The
    /// filter's position in the pipeline comes from
    /// <see cref="MasterReplicaOptions.MvcActionFilterOrder"/>.
    /// <para>
    /// TR: <c>AddControllers()</c> ile arasındaki sıra önemli değildir: bu metot
    /// <see cref="MvcOptions"/> ayarlarını options hattı üzerinden yapılandırır ve MVC bunları
    /// kurulurken okur. Filtrenin hattaki konumu
    /// <see cref="MasterReplicaOptions.MvcActionFilterOrder"/> değerinden gelir.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddControllers();
    /// builder.Services.AddDbTargetMvcFilter();
    /// </code>
    /// </example>
    public static IServiceCollection AddDbTargetMvcFilter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MvcOptions>()
            .Configure<IOptions<MasterReplicaOptions>>((mvcOptions, masterReplicaOptions) =>
                mvcOptions.Filters.Add<DbTargetActionFilter>(masterReplicaOptions.Value.MvcActionFilterOrder));

        return services;
    }

    /// <summary>
    /// Fluent counterpart of <see cref="AddDbTargetMvcFilter"/>, for chaining off
    /// <c>AddControllers()</c>.
    /// <para>
    /// TR: <c>AddControllers()</c> üzerinden zincirleme için <see cref="AddDbTargetMvcFilter"/>
    /// metodunun akıcı karşılığı.
    /// </para>
    /// </summary>
    /// <param name="builder">
    /// The MVC builder.
    /// <para>TR: MVC builder'ı.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="builder"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="builder"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="builder"/> <see langword="null"/>.</para>
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddControllers().AddDbTargetRouting();
    /// </code>
    /// </example>
    public static IMvcBuilder AddDbTargetRouting(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddDbTargetMvcFilter();
        return builder;
    }
}
