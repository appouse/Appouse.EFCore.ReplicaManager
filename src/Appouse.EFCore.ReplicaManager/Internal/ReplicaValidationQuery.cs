using System;

namespace Appouse.EFCore.ReplicaManager.Internal;

/// <summary>
/// Picks the cheapest statement that forces a real network round trip, which is the only way to tell
/// a live connection from a pooled handle to a dead server.
/// <para>
/// TR: Gerçek bir ağ turunu zorlayan en ucuz ifadeyi seçer; canlı bir bağlantıyı, ölü bir sunucuya
/// ait havuzlanmış bir tutamaçtan ayırmanın tek yolu budur.
/// </para>
/// </summary>
internal static class ReplicaValidationQuery
{
    /// <summary>Works on SQL Server, PostgreSQL, MySQL and SQLite.</summary>
    internal const string Default = "SELECT 1";

    /// <summary>Oracle has no bare SELECT: every query needs a FROM.</summary>
    internal const string Oracle = "SELECT 1 FROM DUAL";

    /// <summary>
    /// Returns the validation statement for a provider.
    /// <para>TR: Bir sağlayıcı için doğrulama ifadesini döndürür.</para>
    /// </summary>
    /// <param name="configured">
    /// The statement configured explicitly, if any. Always wins.
    /// <para>TR: Varsa açıkça yapılandırılmış ifade. Her zaman kazanır.</para>
    /// </param>
    /// <param name="providerName">
    /// The EF Core provider name, used only to tell Oracle apart.
    /// <para>TR: Yalnızca Oracle'ı ayırt etmek için kullanılan EF Core sağlayıcı adı.</para>
    /// </param>
    /// <returns>
    /// The statement to execute.
    /// <para>TR: Çalıştırılacak ifade.</para>
    /// </returns>
    internal static string For(string? configured, string? providerName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return providerName?.Contains("Oracle", StringComparison.OrdinalIgnoreCase) == true
            ? Oracle
            : Default;
    }
}
