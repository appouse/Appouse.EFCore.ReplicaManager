using System;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Routes MVC controller actions and Razor Page handlers to the master or to a replica, based on
/// <c>[UseWriteDb]</c>/<c>[UseReadDb]</c> and, failing that, on the HTTP verb.
/// </summary>
/// <remarks>
/// <para>
/// Registered globally by <c>services.AddDbTargetMvcFilter()</c> with
/// <see cref="ReadWriteOptions.MvcActionFilterOrder"/>, so the target is already pinned before any
/// other filter, model binder or action code runs.
/// </para>
/// <para>
/// The scope is opened with <c>using</c> around <c>await next()</c> inside a single
/// <see langword="async"/> method. That is not stylistic: the asynchronous filter contract is the
/// only one that guarantees the ambient value flows into the action and is restored afterwards.
/// The synchronous <see cref="IActionFilter"/> contract sets the value in one method and would have
/// to restore it in another, after an <see langword="await"/> has already reverted the execution
/// context - which silently loses the change.
/// </para>
/// </remarks>
public sealed class DbTargetActionFilter : IAsyncActionFilter, IAsyncPageFilter
{
    private readonly ReadWriteOptions _options;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the filter.
    /// </summary>
    /// <param name="targetContext">Ambient store holding the target for the current flow.</param>
    /// <param name="options">The configured read/write splitting options.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public DbTargetActionFilter(IDbTargetContext targetContext, IOptions<ReadWriteOptions> options)
    {
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(options);

        _targetContext = targetContext;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var target = HttpTargetResolver.Resolve(
            HttpTargetResolver.FromMetadata(context.ActionDescriptor.EndpointMetadata),
            context.HttpContext.Request.Method,
            _options,
            _targetContext.CurrentTarget);

        using (_targetContext.UseTarget(target))
        {
            await next().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var target = HttpTargetResolver.Resolve(
            HttpTargetResolver.FromMetadata(context.ActionDescriptor.EndpointMetadata),
            context.HttpContext.Request.Method,
            _options,
            _targetContext.CurrentTarget);

        using (_targetContext.UseTarget(target))
        {
            await next().ConfigureAwait(false);
        }
    }
}
