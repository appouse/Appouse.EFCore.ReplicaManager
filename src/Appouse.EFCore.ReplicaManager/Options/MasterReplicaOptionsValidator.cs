using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Validates <see cref="MasterReplicaOptions"/> through the options pipeline, so a misconfigured
/// application refuses to start instead of failing on its first query.
/// <para>
/// TR: <see cref="MasterReplicaOptions"/> ayarlarını options hattı üzerinden doğrular; böylece
/// hatalı yapılandırılmış bir uygulama ilk sorguda değil, hiç başlamayarak hata verir.
/// </para>
/// </summary>
/// <remarks>
/// Validation lives here rather than as argument checks inside the registration extension method.
/// Argument checks only observe what the configuration lambda set at registration time: they cannot
/// see values bound later from <c>appsettings.json</c>, contributions from other
/// <see cref="IConfigureOptions{TOptions}"/> registrations, or reloaded configuration - which
/// produces both false passes and false failures.
/// <para>
/// TR: Doğrulama, kayıt uzantı metodunun içindeki argüman kontrolleri yerine burada yapılır. Argüman
/// kontrolleri yalnızca kayıt anında lambda'nın belirlediği değerleri görür; <c>appsettings.json</c>
/// üzerinden sonradan bağlanan değerleri, diğer <see cref="IConfigureOptions{TOptions}"/>
/// kayıtlarının katkılarını veya yeniden yüklenen yapılandırmayı göremez. Bu da hem yanlış geçişlere
/// hem yanlış başarısızlıklara yol açar.
/// </para>
/// </remarks>
internal sealed class MasterReplicaOptionsValidator : IValidateOptions<MasterReplicaOptions>
{
    /// <summary>
    /// Checks the configured topology and returns every problem at once.
    /// <para>TR: Yapılandırılmış topolojiyi denetler ve tüm sorunları tek seferde döndürür.</para>
    /// </summary>
    /// <param name="name">
    /// The named options instance being validated.
    /// <para>TR: Doğrulanan adlandırılmış options örneği.</para>
    /// </param>
    /// <param name="options">
    /// The options to validate.
    /// <para>TR: Doğrulanacak ayarlar.</para>
    /// </param>
    /// <returns>
    /// Success, or a failure carrying one message per problem.
    /// <para>TR: Başarı; veya her sorun için bir mesaj taşıyan başarısızlık sonucu.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="options"/> <see langword="null"/>.</para>
    /// </exception>
    public ValidateOptionsResult Validate(string? name, MasterReplicaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.MasterConnectionString))
        {
            failures.Add(
                $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.MasterConnectionString)} must be set " +
                "to the master connection string.");
        }

        if (string.IsNullOrWhiteSpace(options.ReplicaConnectionString) &&
            options.ReplicaConnectionStrings.Count == 0)
        {
            failures.Add(
                $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.ReplicaConnectionString)} must be set, " +
                $"or at least one entry must be added to {nameof(MasterReplicaOptions.ReplicaConnectionStrings)}. " +
                "To run without a replica - in development, for instance - set it to the same value as " +
                $"{nameof(MasterReplicaOptions.MasterConnectionString)}.");
        }

        for (var i = 0; i < options.ReplicaConnectionStrings.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(options.ReplicaConnectionStrings[i]))
            {
                failures.Add(
                    $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.ReplicaConnectionStrings)}[{i}] " +
                    "is blank.");
            }
        }

        if (!Enum.IsDefined(options.DefaultTarget))
        {
            failures.Add(
                $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.DefaultTarget)} " +
                $"('{(int)options.DefaultTarget}') is not a defined {nameof(DbTarget)} value.");
        }

        if (options.ReplicaFailureCooldown < TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.ReplicaFailureCooldown)} " +
                "cannot be negative.");
        }

        if (options.StickToMasterAfterSaveChanges && !options.ForceMasterOnSaveChanges)
        {
            failures.Add(
                $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.StickToMasterAfterSaveChanges)} " +
                $"requires {nameof(MasterReplicaOptions.ForceMasterOnSaveChanges)} to be enabled.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
