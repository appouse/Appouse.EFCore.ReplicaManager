using System;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Routes an entire request - controllers, Razor Pages, Minimal APIs, gRPC and anything else -
/// in a single place, by reading the selected endpoint's metadata and the HTTP verb.
/// </summary>
/// <remarks>
/// <para>
/// Enable it with <c>app.UseDbTargetRouting()</c>. It must sit <em>after</em> <c>UseRouting()</c>,
/// because before routing runs there is no selected endpoint and every
/// <c>[UseWriteDb]</c>/<c>[UseReadDb]</c> attribute would be invisible - the request would silently
/// fall back to the verb convention. It must also sit <em>before</em> the endpoint executes.
/// </para>
/// <para>
/// Use this instead of <see cref="DbTargetActionFilter"/> when you want one uniform rule for a
/// mixed application; in that case simply do not call <c>services.AddDbTargetMvcFilter()</c>, so the
/// target is not pinned twice.
/// </para>
/// </remarks>
public sealed class DbTargetMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ReadWriteOptions _options;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the middleware.
    /// </summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="targetContext">Ambient store holding the target for the current flow.</param>
    /// <param name="options">The configured read/write splitting options.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public DbTargetMiddleware(RequestDelegate next, IDbTargetContext targetContext, IOptions<ReadWriteOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(options);

        _next = next;
        _targetContext = targetContext;
        _options = options.Value;
    }

    /// <summary>
    /// Pins the target for the duration of the request and invokes the rest of the pipeline.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the pipeline has finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var target = HttpTargetResolver.Resolve(
            HttpTargetResolver.FromMetadata(context.GetEndpoint()?.Metadata),
            context.Request.Method,
            _options,
            _targetContext.CurrentTarget);

        using (_targetContext.UseTarget(target))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
