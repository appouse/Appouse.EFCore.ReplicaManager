using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Records which HTTP routing mechanism the application wired up, so start-up validation can tell a
/// deliberate choice from a forgotten call.
/// <para>
/// TR: Uygulamanın hangi HTTP yönlendirme mekanizmasını bağladığını kaydeder; böylece açılış
/// doğrulaması bilinçli bir tercihi unutulmuş bir çağrıdan ayırt edebilir.
/// </para>
/// </summary>
/// <remarks>
/// Both mechanisms are valid, and using neither is valid too - an application may route purely
/// through <see cref="IDbTargetContext.UseTarget"/> scopes. What is almost never intended is
/// registering controllers and then forgetting both, which leaves every action on
/// <see cref="MasterReplicaOptions.DefaultTarget"/> with no error to explain it.
/// <para>
/// TR: Her iki mekanizma da geçerlidir; hiçbirini kullanmamak da geçerlidir - bir uygulama tamamen
/// <see cref="IDbTargetContext.UseTarget"/> scope'ları üzerinden yönlendirme yapabilir. Neredeyse
/// hiçbir zaman istenmeyen şey, controller'ları kaydedip ikisini birden unutmaktır; bu durumda tüm
/// action'lar <see cref="MasterReplicaOptions.DefaultTarget"/> üzerinde kalır ve bunu açıklayan bir
/// hata çıkmaz.
/// </para>
/// </remarks>
public sealed class DbTargetRoutingMarker
{
    /// <summary>
    /// Set when <c>services.AddDbTargetMvcFilter()</c> has run.
    /// <para>TR: <c>services.AddDbTargetMvcFilter()</c> çağrıldığında işaretlenir.</para>
    /// </summary>
    public bool MvcFilterRegistered { get; internal set; }

    /// <summary>
    /// Set when <c>app.UseDbTargetRouting()</c> has run.
    /// <para>TR: <c>app.UseDbTargetRouting()</c> çağrıldığında işaretlenir.</para>
    /// </summary>
    public bool MiddlewareRegistered { get; set; }

    /// <summary>
    /// Returns the single marker instance for this application, creating and registering it when
    /// this is the first call.
    /// <para>
    /// TR: Bu uygulamaya ait tek marker örneğini döndürür; ilk çağrıysa oluşturup kaydeder.
    /// </para>
    /// </summary>
    /// <param name="services">
    /// The service collection being configured.
    /// <para>TR: Yapılandırılmakta olan servis koleksiyonu.</para>
    /// </param>
    /// <returns>
    /// The shared marker.
    /// <para>TR: Paylaşılan marker.</para>
    /// </returns>
    /// <remarks>
    /// Registered as an instance rather than a type so the registration extensions can write to it
    /// before the service provider exists, in any order.
    /// <para>
    /// TR: Kayıt uzantılarının, servis sağlayıcı henüz yokken ve herhangi bir sırada üzerine
    /// yazabilmesi için tür yerine örnek olarak kaydedilir.
    /// </para>
    /// </remarks>
    internal static DbTargetRoutingMarker GetOrAdd(IServiceCollection services)
    {
        var existing = services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(DbTargetRoutingMarker))?
            .ImplementationInstance as DbTargetRoutingMarker;

        if (existing is not null)
        {
            return existing;
        }

        var marker = new DbTargetRoutingMarker();
        services.AddSingleton(marker);
        return marker;
    }
}
