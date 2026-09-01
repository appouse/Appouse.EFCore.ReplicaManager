using System;
using Appouse.EFCore.ReplicaManager;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Pipeline integration for read/write splitting.
/// </summary>
public static class ReplicaManagerApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="DbTargetMiddleware"/>, which routes every request - controllers, Razor
    /// Pages, Minimal APIs and anything else - from one place.
    /// </summary>
    /// <param name="app">The application pipeline.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Must be placed <em>after</em> <c>UseRouting()</c>. Before routing there is no selected
    /// endpoint, so every <c>[UseWriteDb]</c>/<c>[UseReadDb]</c> attribute would be invisible and
    /// requests would silently fall back to the HTTP verb convention. In a Minimal API app that
    /// calls <c>app.MapGet(...)</c> without an explicit <c>UseRouting()</c>, place this call before
    /// the first <c>Map*</c> call and ASP.NET Core inserts routing in the right place for you.
    /// </remarks>
    /// <example>
    /// <code>
    /// app.UseRouting();
    /// app.UseDbTargetRouting();
    /// app.MapControllers();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseDbTargetRouting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<DbTargetMiddleware>();
    }
}
