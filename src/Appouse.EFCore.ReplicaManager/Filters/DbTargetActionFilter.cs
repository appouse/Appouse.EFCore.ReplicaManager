using System;
using System.Threading.Tasks;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Routes MVC controller actions and Razor Page handlers to the master or to a replica, based on
/// <c>[UseMasterDb]</c>/<c>[UseReplicaDb]</c> and, failing that, on the HTTP verb.
/// <para>
/// TR: MVC controller action'larını ve Razor Page handler'larını
/// <c>[UseMasterDb]</c>/<c>[UseReplicaDb]</c> attribute'larına, bunlar yoksa HTTP metoduna göre
/// master'a veya bir replica'ya yönlendirir.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Registered globally by <c>services.AddDbTargetMvcFilter()</c> with
/// <see cref="MasterReplicaOptions.MvcActionFilterOrder"/>, so the target is already pinned before any
/// other filter, model binder or action code runs.
/// </para>
/// <para>
/// TR: <c>services.AddDbTargetMvcFilter()</c> ile ve
/// <see cref="MasterReplicaOptions.MvcActionFilterOrder"/> sırasıyla global olarak kaydedilir; böylece
/// hedef, diğer filtreler, model binder'lar ve action kodu çalışmadan önce sabitlenir.
/// </para>
/// <para>
/// The scope is opened with <c>using</c> around <c>await next()</c> inside a single asynchronous
/// method. That is not stylistic: the asynchronous filter contract is the only one that guarantees
/// the ambient value flows into the action and is restored afterwards. The synchronous
/// <see cref="IActionFilter"/> contract sets the value in one method and would have to restore it in
/// another, after an <see langword="await"/> has already reverted the execution context - which
/// silently loses the change.
/// </para>
/// <para>
/// TR: Scope, tek bir asenkron metot içinde <c>await next()</c> çevresinde <c>using</c> ile açılır. Bu
/// bir üslup tercihi değildir: ortam değerinin action'a akmasını ve sonrasında geri yüklenmesini
/// garanti eden tek sözleşme asenkron filtre sözleşmesidir. Senkron <see cref="IActionFilter"/>
/// sözleşmesi değeri bir metotta belirleyip başka bir metotta geri yüklemek zorunda kalır; bu sırada
/// bir <see langword="await"/> yürütme bağlamını çoktan geri almış olur ve değişiklik sessizce
/// kaybolur.
/// </para>
/// </remarks>
public sealed class DbTargetActionFilter : IAsyncActionFilter, IAsyncPageFilter
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
    public DbTargetActionFilter(IDbTargetContext targetContext, IOptions<MasterReplicaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(options);

        _targetContext = targetContext;
        _options = options.Value;
    }

    /// <summary>
    /// Pins the target for the duration of an MVC action and invokes the rest of the filter pipeline.
    /// <para>
    /// TR: Bir MVC action'ı süresince hedefi sabitler ve filtre hattının geri kalanını çalıştırır.
    /// </para>
    /// </summary>
    /// <param name="context">
    /// The action being executed.
    /// <para>TR: Çalıştırılan action.</para>
    /// </param>
    /// <param name="next">
    /// The rest of the pipeline.
    /// <para>TR: Hattın geri kalanı.</para>
    /// </param>
    /// <returns>
    /// A task that completes when the action and its remaining filters have finished.
    /// <para>TR: Action ve kalan filtreleri tamamlandığında biten görev.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
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

    /// <summary>
    /// Runs before a Razor Page handler is selected. Nothing to do here; routing happens once the
    /// handler is known.
    /// <para>
    /// TR: Razor Page handler'ı seçilmeden önce çalışır. Burada yapılacak bir şey yoktur; yönlendirme,
    /// handler belli olduktan sonra gerçekleşir.
    /// </para>
    /// </summary>
    /// <param name="context">
    /// The page whose handler is being selected.
    /// <para>TR: Handler'ı seçilen sayfa.</para>
    /// </param>
    /// <returns>
    /// A completed task.
    /// <para>TR: Tamamlanmış bir görev.</para>
    /// </returns>
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    /// <summary>
    /// Pins the target for the duration of a Razor Page handler and invokes the rest of the pipeline.
    /// <para>
    /// TR: Bir Razor Page handler'ı süresince hedefi sabitler ve hattın geri kalanını çalıştırır.
    /// </para>
    /// </summary>
    /// <param name="context">
    /// The page handler being executed.
    /// <para>TR: Çalıştırılan sayfa handler'ı.</para>
    /// </param>
    /// <param name="next">
    /// The rest of the pipeline.
    /// <para>TR: Hattın geri kalanı.</para>
    /// </param>
    /// <returns>
    /// A task that completes when the handler and its remaining filters have finished.
    /// <para>TR: Handler ve kalan filtreleri tamamlandığında biten görev.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
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
