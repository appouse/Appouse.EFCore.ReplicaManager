using System;
using Appouse.EFCore.ReplicaManager;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Pipeline integration for master/replica splitting.
/// <para>TR: Master/replica ayrımı için istek hattı entegrasyonu.</para>
/// </summary>
public static class ReplicaManagerApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="DbTargetMiddleware"/>, which routes every request - controllers, Razor Pages,
    /// Minimal APIs and anything else - from one place.
    /// <para>
    /// TR: Her isteği - controller'lar, Razor Pages, Minimal API'ler ve diğer her şey - tek bir yerden
    /// yönlendiren <see cref="DbTargetMiddleware"/> bileşenini ekler.
    /// </para>
    /// </summary>
    /// <param name="app">
    /// The application pipeline.
    /// <para>TR: Uygulama istek hattı.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="app"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="app"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="app"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="app"/> <see langword="null"/>.</para>
    /// </exception>
    /// <remarks>
    /// Must be placed <em>after</em> <c>UseRouting()</c>. Before routing there is no selected endpoint,
    /// so every <c>[UseMasterDb]</c>/<c>[UseReplicaDb]</c> attribute would be invisible and requests
    /// would silently fall back to the HTTP verb convention.
    /// <para>
    /// TR: <c>UseRouting()</c> çağrısından <em>sonra</em> yer almalıdır. Yönlendirmeden önce seçilmiş
    /// bir endpoint yoktur; bu yüzden tüm <c>[UseMasterDb]</c>/<c>[UseReplicaDb]</c> attribute'ları
    /// görünmez olur ve istekler sessizce HTTP metodu konvansiyonuna düşer.
    /// </para>
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
