using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Default <see cref="IDbConnectionStringResolver"/>: serves the master connection string for
/// <see cref="DbTarget.WriteMaster"/> and delegates replica choice to
/// <see cref="IReadReplicaSelector"/> for <see cref="DbTarget.ReadReplica"/>.
/// </summary>
/// <remarks>
/// The replica list is materialised once at construction time -
/// <see cref="ReadWriteOptions.ReadConnectionString"/> first, then every non-blank entry of
/// <see cref="ReadWriteOptions.ReadConnectionStrings"/> - so resolving a connection string on the
/// hot path allocates nothing.
/// </remarks>
public sealed class DbConnectionStringResolver : IDbConnectionStringResolver
{
    private readonly bool _allowFallbackToWrite;
    private readonly IReadOnlyList<string> _replicas;
    private readonly IReadReplicaSelector _selector;
    private readonly string _writeConnectionString;

    /// <summary>
    /// Creates a resolver over the configured connection strings.
    /// </summary>
    /// <param name="options">The configured read/write splitting options.</param>
    /// <param name="selector">Strategy used to pick between multiple replicas.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public DbConnectionStringResolver(IOptions<ReadWriteOptions> options, IReadReplicaSelector selector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selector);

        var value = options.Value;
        _selector = selector;
        _writeConnectionString = value.WriteConnectionString;
        _allowFallbackToWrite = value.AllowReadFallbackToWrite;

        var replicas = new List<string>(value.ReadConnectionStrings.Count + 1);
        if (!string.IsNullOrWhiteSpace(value.ReadConnectionString))
        {
            replicas.Add(value.ReadConnectionString);
        }

        foreach (var connectionString in value.ReadConnectionStrings)
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                replicas.Add(connectionString);
            }
        }

        _replicas = replicas;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// A replica was requested, none is configured, and
    /// <see cref="ReadWriteOptions.AllowReadFallbackToWrite"/> is disabled.
    /// </exception>
    public string Resolve(DbTarget target)
    {
        if (target == DbTarget.WriteMaster)
        {
            return _writeConnectionString;
        }

        if (_replicas.Count == 0)
        {
            return _allowFallbackToWrite
                ? _writeConnectionString
                : throw new InvalidOperationException(
                    $"{DbTarget.ReadReplica} was requested but no replica connection string is configured. " +
                    $"Set {nameof(ReadWriteOptions)}.{nameof(ReadWriteOptions.ReadConnectionString)}, or enable " +
                    $"{nameof(ReadWriteOptions)}.{nameof(ReadWriteOptions.AllowReadFallbackToWrite)}.");
        }

        if (_replicas.Count == 1)
        {
            return _replicas[0];
        }

        var selected = _selector.Select(_replicas);
        return string.IsNullOrWhiteSpace(selected) ? _replicas[0] : selected;
    }
}
