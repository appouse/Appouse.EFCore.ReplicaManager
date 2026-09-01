using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.IntegrationTests;

public sealed class Marker
{
    public int Id { get; set; }

    public string Source { get; set; } = string.Empty;
}

public sealed class MarkerContext(DbContextOptions<MarkerContext> options) : DbContext(options)
{
    public DbSet<Marker> Markers => Set<Marker>();
}

/// <summary>
/// Three real database servers in containers - one master and two replicas - each seeded with the
/// same schema but a different marker row. Reading the row back is how a test proves which physical
/// server actually answered, rather than which one the package intended to use.
/// </summary>
/// <remarks>
/// These are three independent servers rather than a real replication cluster, and that is
/// deliberate: streaming replication would make every node return identical data, which is precisely
/// what would make the routing unobservable. Replication itself is the database's job, not this
/// package's.
/// </remarks>
public abstract class LiveClusterFixture : IAsyncLifetime
{
    public const string MasterSource = "master";
    public const string FirstReplicaSource = "replica-1";
    public const string SecondReplicaSource = "replica-2";

    private readonly List<string> _containers = [];

    protected abstract string Image { get; }

    protected abstract string Prefix { get; }

    protected abstract int BasePort { get; }

    protected abstract int ContainerPort { get; }

    protected abstract string RunArguments { get; }

    /// <summary>
    /// What to hand <c>docker pull</c>. Overridden where an explicit platform is needed, as for the
    /// SQL Server image, which ships amd64 only and runs under emulation on Apple Silicon.
    /// </summary>
    protected virtual string PullArguments => Image;

    protected abstract string CreateTableSql { get; }

    protected abstract TimeSpan ReadyTimeout { get; }

    public string MasterConnectionString => ConnectionStringFor(BasePort);

    public string FirstReplicaConnectionString => ConnectionStringFor(BasePort + 1);

    public string SecondReplicaConnectionString => ConnectionStringFor(BasePort + 2);

    public string FirstReplicaContainer => $"{Prefix}-1";

    public string SecondReplicaContainer => $"{Prefix}-2";

    public abstract string ConnectionStringFor(int hostPort);

    public abstract DbConnection CreateConnection(string connectionString);

    public abstract void Configure(DbContextOptionsBuilder options, string connectionString);

    /// <summary>
    /// The same provider call with EF Core's retrying execution strategy enabled - the configuration
    /// this package recommends alongside it, and the one that makes a replica outage invisible to
    /// application code.
    /// </summary>
    public abstract void ConfigureWithRetry(DbContextOptionsBuilder options, string connectionString);

    protected virtual string InsertSourceSql(string source) => $"INSERT INTO {QuotedTable} ({QuotedColumn}) VALUES ('{source}')";

    protected abstract string QuotedTable { get; }

    protected abstract string QuotedColumn { get; }

    public async Task InitializeAsync()
    {
        if (!Docker.IsAvailable)
        {
            return;
        }

        Docker.Run($"pull -q {PullArguments}");

        var sources = new[] { MasterSource, FirstReplicaSource, SecondReplicaSource };

        for (var i = 0; i < sources.Length; i++)
        {
            var name = $"{Prefix}-{i}";
            var port = BasePort + i;

            Docker.RemoveQuietly(name);
            Docker.Run($"run -d --name {name} {RunArguments} -p 127.0.0.1:{port}:{ContainerPort} {Image}");
            _containers.Add(name);
        }

        for (var i = 0; i < sources.Length; i++)
        {
            await SeedAsync(ConnectionStringFor(BasePort + i), sources[i]);
        }
    }

    public Task DisposeAsync()
    {
        foreach (var container in _containers)
        {
            Docker.RemoveQuietly(container);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits for the server to accept connections, then creates the table and inserts the one row
    /// that identifies this node.
    /// </summary>
    private async Task SeedAsync(string connectionString, string source)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = CreateConnection(connectionString);
                await connection.OpenAsync();

                await using (var create = connection.CreateCommand())
                {
                    create.CommandText = CreateTableSql;
                    await create.ExecuteNonQueryAsync();
                }

                await using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = InsertSourceSql(source);
                    await insert.ExecuteNonQueryAsync();
                }

                return;
            }
            catch (Exception exception)
            {
                last = exception;
                await Task.Delay(1000);
            }
        }

        throw new InvalidOperationException(
            $"{Image} did not become ready within {ReadyTimeout}. Last error: {last?.Message}", last);
    }
}
