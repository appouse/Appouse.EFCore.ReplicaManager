using System.Collections.Generic;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Supplies the connection strings of the topology: exactly one master, and zero or more replicas.
/// <para>
/// TR: Topolojinin bağlantı dizelerini sağlar: tam olarak bir master ve sıfır veya daha fazla
/// replica.
/// </para>
/// </summary>
/// <remarks>
/// Replace the default registration to source connection strings from somewhere other than
/// <see cref="MasterReplicaOptions"/> - a multi-tenant catalogue, a secret store, or service
/// discovery:
/// <code>
/// services.Replace(ServiceDescriptor.Singleton&lt;IDbConnectionStringResolver, TenantConnectionStringResolver&gt;());
/// </code>
/// Implementations must be thread-safe and are resolved as singletons. The replica list must keep a
/// stable order and a stable length for the lifetime of the application, because
/// <see cref="IReplicaHealthMonitor"/> tracks availability by index.
/// <para>
/// TR: Bağlantı dizelerini <see cref="MasterReplicaOptions"/> dışında bir kaynaktan - çok kiracılı
/// bir katalog, bir sır deposu veya servis keşfi - almak için varsayılan kaydı değiştirin.
/// Uygulamalar thread-safe olmalıdır ve singleton olarak çözümlenir. Replica listesi uygulamanın
/// ömrü boyunca sabit sırada ve sabit uzunlukta kalmalıdır; çünkü
/// <see cref="IReplicaHealthMonitor"/> erişilebilirliği indekse göre izler.
/// </para>
/// </remarks>
public interface IDbConnectionStringResolver
{
    /// <summary>
    /// Returns the connection string of the single master database.
    /// <para>TR: Tek master veritabanının bağlantı dizesini döndürür.</para>
    /// </summary>
    /// <returns>
    /// A non-empty connection string.
    /// <para>TR: Boş olmayan bir bağlantı dizesi.</para>
    /// </returns>
    string GetMasterConnectionString();

    /// <summary>
    /// Returns the configured replica connection strings, in a stable order.
    /// <para>TR: Tanımlı replica bağlantı dizelerini sabit bir sırayla döndürür.</para>
    /// </summary>
    /// <returns>
    /// The replicas, possibly empty when none is configured.
    /// <para>TR: Replica listesi; hiçbiri tanımlı değilse boş olabilir.</para>
    /// </returns>
    IReadOnlyList<string> GetReplicaConnectionStrings();
}
