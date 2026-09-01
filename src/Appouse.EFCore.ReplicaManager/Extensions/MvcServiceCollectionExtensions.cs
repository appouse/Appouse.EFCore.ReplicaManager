using System;
using Appouse.EFCore.ReplicaManager;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Opt-in MVC and Razor Pages integration.
/// </summary>
/// <remarks>
/// Kept apart from
/// <see cref="ReplicaManagerServiceCollectionExtensions.AddEfCoreReadWriteSplit(IServiceCollection,Action{ReadWriteOptions},bool)"/>
/// so that non-web hosts never JIT a method that references an MVC type. Call these only from an
/// application that has the ASP.NET Core shared framework - which any application calling
/// <c>AddControllers()</c> already does.
/// </remarks>
public static class ReplicaManagerMvcServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="DbTargetActionFilter"/> to the global MVC filter collection, so every
    /// controller action and Razor Page handler is routed by attribute or HTTP verb.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Order does not matter relative to <c>AddControllers()</c>: this configures
    /// <see cref="MvcOptions"/> through the options pipeline, which MVC reads when it is built.
    /// The filter's position in the pipeline comes from
    /// <see cref="ReadWriteOptions.MvcActionFilterOrder"/>.
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
            .Configure<IOptions<ReadWriteOptions>>((mvcOptions, readWriteOptions) =>
                mvcOptions.Filters.Add<DbTargetActionFilter>(readWriteOptions.Value.MvcActionFilterOrder));

        return services;
    }

    /// <summary>
    /// Fluent counterpart of <see cref="AddDbTargetMvcFilter"/>, for chaining off
    /// <c>AddControllers()</c>.
    /// </summary>
    /// <param name="builder">The MVC builder.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
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
