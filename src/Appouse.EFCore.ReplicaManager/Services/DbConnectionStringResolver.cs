using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Default <see cref="IDbConnectionStringResolver"/>: serves the connection strings configured in
/// <see cref="MasterReplicaOptions"/>.
/// <para>
/// TR: Varsayılan <see cref="IDbConnectionStringResolver"/> uygulaması:
/// <see cref="MasterReplicaOptions"/> içinde tanımlanan bağlantı dizelerini sunar.
/// </para>
/// </summary>
/// <remarks>
/// The replica list is materialised once at construction -
/// <see cref="MasterReplicaOptions.ReplicaConnectionString"/> first, then every non-blank entry of
/// <see cref="MasterReplicaOptions.ReplicaConnectionStrings"/> - so its order and length are stable,
/// which is what lets <see cref="IReplicaHealthMonitor"/> track availability by index.
/// <para>
/// TR: Replica listesi kurulum anında bir kez oluşturulur - önce
/// <see cref="MasterReplicaOptions.ReplicaConnectionString"/>, ardından
/// <see cref="MasterReplicaOptions.ReplicaConnectionStrings"/> içindeki boş olmayan her kayıt.
/// Böylece sırası ve uzunluğu sabit kalır; <see cref="IReplicaHealthMonitor"/>'ın erişilebilirliği
/// indekse göre izleyebilmesini sağlayan da budur.
/// </para>
/// </remarks>
public sealed class DbConnectionStringResolver : IDbConnectionStringResolver
{
    private readonly string _masterConnectionString;
    private readonly IReadOnlyList<string> _replicaConnectionStrings;

    /// <summary>
    /// Creates a resolver over the configured connection strings.
    /// <para>TR: Yapılandırılmış bağlantı dizeleri üzerinde bir çözümleyici oluşturur.</para>
    /// </summary>
    /// <param name="options">
    /// The configured master/replica options.
    /// <para>TR: Yapılandırılmış master/replica ayarları.</para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="options"/> <see langword="null"/>.</para>
    /// </exception>
    public DbConnectionStringResolver(IOptions<MasterReplicaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        _masterConnectionString = value.MasterConnectionString;

        var replicas = new List<string>(value.ReplicaConnectionStrings.Count + 1);
        if (!string.IsNullOrWhiteSpace(value.ReplicaConnectionString))
        {
            replicas.Add(value.ReplicaConnectionString);
        }

        foreach (var connectionString in value.ReplicaConnectionStrings)
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                replicas.Add(connectionString);
            }
        }

        _replicaConnectionStrings = replicas;
    }

    /// <inheritdoc />
    public string GetMasterConnectionString() => _masterConnectionString;

    /// <inheritdoc />
    public IReadOnlyList<string> GetReplicaConnectionStrings() => _replicaConnectionStrings;
}
