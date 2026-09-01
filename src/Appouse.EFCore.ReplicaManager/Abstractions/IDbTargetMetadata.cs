namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Marks endpoint metadata - typically an attribute - that pins an MVC action, a Razor Page handler
/// or a Minimal API endpoint to a specific <see cref="DbTarget"/>.
/// <para>
/// TR: Bir MVC action'ını, Razor Page handler'ını veya Minimal API endpoint'ini belirli bir
/// <see cref="DbTarget"/> hedefine sabitleyen endpoint metadata'sını - genellikle bir attribute -
/// işaretler.
/// </para>
/// </summary>
/// <remarks>
/// Both <see cref="UseMasterDbAttribute"/> and <see cref="UseReplicaDbAttribute"/> implement this
/// interface, so the routing components resolve either with a single
/// <c>GetMetadata&lt;IDbTargetMetadata&gt;()</c> lookup. Because that method returns the
/// <em>last</em> matching item and ASP.NET Core appends action-level metadata after controller-level
/// metadata, an attribute on the action automatically wins over one on the controller.
/// <para>
/// TR: <see cref="UseMasterDbAttribute"/> ve <see cref="UseReplicaDbAttribute"/> bu arabirimi
/// uyguladığı için, yönlendirme bileşenleri tek bir <c>GetMetadata&lt;IDbTargetMetadata&gt;()</c>
/// çağrısıyla ikisini de bulabilir. Bu metot eşleşen <em>son</em> öğeyi döndürdüğünden ve ASP.NET
/// Core action seviyesindeki metadata'yı controller seviyesindekinden sonra eklediğinden, action
/// üzerindeki attribute controller üzerindekini kendiliğinden yener.
/// </para>
/// </remarks>
public interface IDbTargetMetadata
{
    /// <summary>
    /// Gets the database target the decorated endpoint must run against.
    /// <para>TR: İşaretlenmiş endpoint'in çalışacağı veritabanı hedefini verir.</para>
    /// </summary>
    DbTarget Target { get; }
}
