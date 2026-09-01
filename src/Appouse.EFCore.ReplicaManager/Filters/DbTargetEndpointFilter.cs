using System;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// The Minimal API counterpart of <see cref="DbTargetActionFilter"/>. Attach it to a single endpoint,
/// or to a whole route group, with <c>AddEndpointFilter&lt;DbTargetEndpointFilter&gt;()</c> or the
/// <c>UseMasterDb()</c>/<c>UseReplicaDb()</c> helpers.
/// <para>
/// TR: <see cref="DbTargetActionFilter"/>'ın Minimal API karşılığı. Tek bir endpoint'e veya tüm bir
/// route grubuna <c>AddEndpointFilter&lt;DbTargetEndpointFilter&gt;()</c> ya da
/// <c>UseMasterDb()</c>/<c>UseReplicaDb()</c> yardımcılarıyla eklenir.
/// </para>
/// </summary>
public sealed class DbTargetEndpointFilter : IEndpointFilter
{
    private readonly MasterReplicaOptions _options;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the filter.
    /// <para>TR: Filtreyi oluşturur.</para>
    /// </summary>
    /// <param name="targetContext">
    /// Ambient store holding the target for the current flow.
    /// <para>TR: Geçerli akışın hedefini tutan ortam deposu.</para>
    /// </param>
    /// <param name="options">
    /// The configured master/replica options.
    /// <para>TR: Yapılandırılmış master/replica ayarları.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    public DbTargetEndpointFilter(IDbTargetContext targetContext, IOptions<MasterReplicaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(options);

        _targetContext = targetContext;
        _options = options.Value;
    }

    /// <summary>
    /// Pins the target for the duration of the endpoint and invokes the rest of the filter chain.
    /// <para>
    /// TR: Endpoint süresince hedefi sabitler ve filtre zincirinin geri kalanını çalıştırır.
    /// </para>
    /// </summary>
    /// <param name="context">
    /// The endpoint invocation.
    /// <para>TR: Endpoint çağrısı.</para>
    /// </param>
    /// <param name="next">
    /// The rest of the filter chain.
    /// <para>TR: Filtre zincirinin geri kalanı.</para>
    /// </param>
    /// <returns>
    /// Whatever the endpoint returned.
    /// <para>TR: Endpoint'in döndürdüğü değer.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var target = HttpTargetResolver.Resolve(
            HttpTargetResolver.FromMetadata(httpContext.GetEndpoint()?.Metadata),
            httpContext.Request.Method,
            _options,
            _targetContext.CurrentTarget);

        using (_targetContext.UseTarget(target))
        {
            return await next(context).ConfigureAwait(false);
        }
    }
}
