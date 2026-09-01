using System;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// The Minimal API counterpart of <see cref="DbTargetActionFilter"/>. Attach it to a single
/// endpoint, or to a whole <see cref="Microsoft.AspNetCore.Routing.RouteGroupBuilder"/>, with
/// <c>AddEndpointFilter&lt;DbTargetEndpointFilter&gt;()</c> or the
/// <c>UseReadDb()</c>/<c>UseWriteDb()</c> helpers.
/// </summary>
public sealed class DbTargetEndpointFilter : IEndpointFilter
{
    private readonly ReadWriteOptions _options;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the filter.
    /// </summary>
    /// <param name="targetContext">Ambient store holding the target for the current flow.</param>
    /// <param name="options">The configured read/write splitting options.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public DbTargetEndpointFilter(IDbTargetContext targetContext, IOptions<ReadWriteOptions> options)
    {
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(options);

        _targetContext = targetContext;
        _options = options.Value;
    }

    /// <inheritdoc />
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
