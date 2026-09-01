using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Validates <see cref="ReadWriteOptions"/> through the options pipeline so that a misconfigured
/// application refuses to start instead of failing on its first query.
/// </summary>
/// <remarks>
/// Validation deliberately lives here rather than as argument checks inside the registration
/// extension method. Argument checks only observe what the configuration lambda set at registration
/// time: they cannot see values bound later from <c>appsettings.json</c>, contributions from other
/// <see cref="IConfigureOptions{TOptions}"/> registrations, or reloaded configuration - which
/// produces both false passes and false failures.
/// </remarks>
internal sealed class ReadWriteOptionsValidator : IValidateOptions<ReadWriteOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ReadWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.WriteConnectionString))
        {
            failures.Add(
                $"{nameof(ReadWriteOptions)}.{nameof(ReadWriteOptions.WriteConnectionString)} must be set to the " +
                "primary (master) connection string.");
        }

        if (string.IsNullOrWhiteSpace(options.ReadConnectionString) && options.ReadConnectionStrings.Count == 0)
        {
            failures.Add(
                $"{nameof(ReadWriteOptions)}.{nameof(ReadWriteOptions.ReadConnectionString)} must be set, or at least " +
                $"one entry must be added to {nameof(ReadWriteOptions.ReadConnectionStrings)}. To run without a " +
                "replica - in development, for instance - set it to the same value as " +
                $"{nameof(ReadWriteOptions.WriteConnectionString)}.");
        }

        for (var i = 0; i < options.ReadConnectionStrings.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(options.ReadConnectionStrings[i]))
            {
                failures.Add(
                    $"{nameof(ReadWriteOptions)}.{nameof(ReadWriteOptions.ReadConnectionStrings)}[{i}] is blank.");
            }
        }

        if (!Enum.IsDefined(options.DefaultTarget))
        {
            failures.Add(
                $"{nameof(ReadWriteOptions)}.{nameof(ReadWriteOptions.DefaultTarget)} " +
                $"('{(int)options.DefaultTarget}') is not a defined {nameof(DbTarget)} value.");
        }

        if (options.StickToWriteAfterSaveChanges && !options.ForceWriteOnSaveChanges)
        {
            failures.Add(
                $"{nameof(ReadWriteOptions)}.{nameof(ReadWriteOptions.StickToWriteAfterSaveChanges)} requires " +
                $"{nameof(ReadWriteOptions.ForceWriteOnSaveChanges)} to be enabled.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
