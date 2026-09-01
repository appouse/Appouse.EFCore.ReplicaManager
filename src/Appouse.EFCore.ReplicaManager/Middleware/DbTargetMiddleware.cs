using System;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Routes an entire request - controllers, Razor Pages, Minimal APIs and anything else - in a single
/// place, by reading the selected endpoint's metadata and the HTTP verb.
/// <para>
/// TR: Seçilen endpoint'in metadata'sını ve HTTP metodunu okuyarak bir isteğin tamamını -
/// controller'lar, Razor Pages, Minimal API'ler ve diğer her şey - tek bir yerde yönlendirir.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Enable it with <c>app.UseDbTargetRouting()</c>. It must sit <em>after</em> <c>UseRouting()</c>,
/// because before routing runs there is no selected endpoint and every
/// <c>[UseMasterDb]</c>/<c>[UseReplicaDb]</c> attribute would be invisible - the request would
/// silently fall back to the verb convention. It must also sit <em>before</em> the endpoint executes.
/// </para>
/// <para>
/// TR: <c>app.UseDbTargetRouting()</c> ile etkinleştirilir. <c>UseRouting()</c> çağrısından
/// <em>sonra</em> yer almalıdır; çünkü yönlendirme çalışmadan önce seçilmiş bir endpoint yoktur ve
/// tüm <c>[UseMasterDb]</c>/<c>[UseReplicaDb]</c> attribute'ları görünmez olur - istek sessizce metot
/// konvansiyonuna düşer. Ayrıca endpoint çalışmadan <em>önce</em> yer almalıdır.
/// </para>
/// <para>
/// Use this instead of <see cref="DbTargetActionFilter"/> when you want one uniform rule for a mixed
/// application; in that case simply do not call <c>services.AddDbTargetMvcFilter()</c>, so the target
/// is not pinned twice.
/// </para>
/// <para>
/// TR: Karma bir uygulamada tek ve tutarlı bir kural istiyorsanız
/// <see cref="DbTargetActionFilter"/> yerine bunu kullanın; o durumda
/// <c>services.AddDbTargetMvcFilter()</c> çağrısını yapmayın ki hedef iki kez sabitlenmesin.
/// </para>
/// </remarks>
public sealed class DbTargetMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MasterReplicaOptions _options;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the middleware.
    /// <para>TR: Middleware'i oluşturur.</para>
    /// </summary>
    /// <param name="next">
    /// The next component in the pipeline.
    /// <para>TR: Hattaki bir sonraki bileşen.</para>
    /// </param>
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
    public DbTargetMiddleware(
        RequestDelegate next,
        IDbTargetContext targetContext,
        IOptions<MasterReplicaOptions> options)
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
    /// <para>TR: İstek süresince hedefi sabitler ve hattın geri kalanını çalıştırır.</para>
    /// </summary>
    /// <param name="context">
    /// The current request.
    /// <para>TR: Geçerli istek.</para>
    /// </param>
    /// <returns>
    /// A task that completes when the pipeline has finished.
    /// <para>TR: Hat tamamlandığında biten görev.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="context"/> <see langword="null"/>.</para>
    /// </exception>
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
