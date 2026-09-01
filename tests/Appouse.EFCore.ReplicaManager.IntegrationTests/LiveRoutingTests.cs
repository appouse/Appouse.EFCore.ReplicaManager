using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.IntegrationTests;

/// <summary>
/// Routing and failover against live database servers. Every assertion reads a marker row back, so
/// it reports which physical server answered rather than which one was intended.
/// </summary>
public abstract class LiveRoutingTests<TCluster> : IClassFixture<TCluster>
    where TCluster : LiveClusterFixture
{
    protected LiveRoutingTests(TCluster cluster) => Cluster = cluster;

    protected TCluster Cluster { get; }

    private ServiceProvider Build(Action<MasterReplicaOptions>? configure = null, bool withRetry = false)
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = Cluster.MasterConnectionString;
            options.ReplicaConnectionString = Cluster.FirstReplicaConnectionString;
            options.ReplicaConnectionStrings.Add(Cluster.SecondReplicaConnectionString);
            options.DefaultTarget = DbTarget.Replica;
            configure?.Invoke(options);
        });
        services.AddMasterReplicaDbContext<MarkerContext>(
            withRetry ? Cluster.ConfigureWithRetry : Cluster.Configure);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Reads the marker, reporting a failure as <see langword="null"/> rather than throwing, so a
    /// test can describe what a sequence of requests actually experienced.
    /// </summary>
    private static async Task<string?> TryReadAsync(IServiceProvider provider)
    {
        try
        {
            return await SourceAsync(provider);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<string> SourceAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
        return await db.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync();
    }

    private static async Task<List<string>> SourcesAsync(IServiceProvider provider, int count)
    {
        var seen = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            seen.Add(await SourceAsync(provider));
        }

        return seen;
    }

    private async Task WaitUntilReachableAsync(string connectionString, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = Cluster.CreateConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception)
            {
                await Task.Delay(300);
            }
        }

        throw new InvalidOperationException("The server did not come back within the timeout.");
    }

    [DockerFact]
    public async Task Reads_go_to_a_replica_and_writes_go_to_the_master()
    {
        await using var provider = Build();

        var source = await SourceAsync(provider);
        Assert.Contains(source, new[] { LiveClusterFixture.FirstReplicaSource, LiveClusterFixture.SecondReplicaSource });

        var written = $"live-{Guid.NewGuid():N}";
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
            db.Markers.Add(new Marker { Source = written });
            await db.SaveChangesAsync();
        }

        // The write must be visible on the master and on neither replica.
        Assert.Contains(written, await ReadAllAsync(Cluster.MasterConnectionString));
        Assert.DoesNotContain(written, await ReadAllAsync(Cluster.FirstReplicaConnectionString));
        Assert.DoesNotContain(written, await ReadAllAsync(Cluster.SecondReplicaConnectionString));
    }

    [DockerFact]
    public async Task Reads_are_spread_across_both_replicas()
    {
        await using var provider = Build();

        var seen = await SourcesAsync(provider, 10);

        Assert.Contains(LiveClusterFixture.FirstReplicaSource, seen);
        Assert.Contains(LiveClusterFixture.SecondReplicaSource, seen);
        Assert.DoesNotContain(LiveClusterFixture.MasterSource, seen);
    }

    /// <summary>
    /// A replica that dies while ADO.NET still holds warm pooled handles to it can hand one of those
    /// handles back from <c>OpenAsync</c> without a network round trip, so the first read after the
    /// outage may still fail - the socket is dead but nothing has discovered it yet. What must hold
    /// is that the failure is not repeated: the node is taken out of rotation immediately.
    /// </summary>
    [DockerFact]
    public async Task A_dead_replica_is_taken_out_of_rotation()
    {
        await using var provider = Build();

        // Warm the pool against both replicas first, so the outage happens under load rather than
        // against an idle pool - which is the situation that actually occurs in production.
        await SourcesAsync(provider, 6);

        Docker.Stop(Cluster.FirstReplicaContainer);
        try
        {
            var results = new List<string?>();
            for (var i = 0; i < 10; i++)
            {
                results.Add(await TryReadAsync(provider));
            }

            // The dead node never serves a read, and once the pool has given up its stale handles
            // every remaining request is answered by the survivor.
            Assert.DoesNotContain(LiveClusterFixture.FirstReplicaSource, results);
            Assert.All(results.TakeLast(5), r => Assert.Equal(LiveClusterFixture.SecondReplicaSource, r));
        }
        finally
        {
            Docker.Start(Cluster.FirstReplicaContainer);
            await WaitUntilReachableAsync(Cluster.FirstReplicaConnectionString, TimeSpan.FromSeconds(60));
        }
    }

    /// <summary>
    /// With EF Core's retrying execution strategy enabled - the configuration this package
    /// recommends - the same outage is invisible to application code, because the retry opens a
    /// fresh connection that routes to a replica already marked down.
    /// </summary>
    [DockerFact]
    public async Task With_retry_enabled_a_replica_outage_is_transparent()
    {
        await using var provider = Build(withRetry: true);
        await SourcesAsync(provider, 6);

        Docker.Stop(Cluster.FirstReplicaContainer);
        try
        {
            var seen = await SourcesAsync(provider, 8);
            Assert.All(seen, s => Assert.Equal(LiveClusterFixture.SecondReplicaSource, s));
        }
        finally
        {
            Docker.Start(Cluster.FirstReplicaContainer);
            await WaitUntilReachableAsync(Cluster.FirstReplicaConnectionString, TimeSpan.FromSeconds(60));
        }
    }

    [DockerFact]
    public async Task When_every_replica_is_stopped_the_master_serves_reads()
    {
        await using var provider = Build(withRetry: true);

        Docker.Stop(Cluster.FirstReplicaContainer);
        Docker.Stop(Cluster.SecondReplicaContainer);
        try
        {
            Assert.Equal(LiveClusterFixture.MasterSource, await SourceAsync(provider));
        }
        finally
        {
            Docker.Start(Cluster.FirstReplicaContainer);
            Docker.Start(Cluster.SecondReplicaContainer);
            await WaitUntilReachableAsync(Cluster.FirstReplicaConnectionString, TimeSpan.FromSeconds(60));
            await WaitUntilReachableAsync(Cluster.SecondReplicaConnectionString, TimeSpan.FromSeconds(60));
        }
    }

    [DockerFact]
    public async Task When_every_replica_is_stopped_and_fallback_is_disabled_it_throws()
    {
        await using var provider = Build(options => options.AllowReplicaFallbackToMaster = false, withRetry: true);

        Docker.Stop(Cluster.FirstReplicaContainer);
        Docker.Stop(Cluster.SecondReplicaContainer);
        try
        {
            var error = await Assert.ThrowsAsync<ReplicaUnavailableException>(() => SourceAsync(provider));
            var causes = Assert.IsType<AggregateException>(error.InnerException);
            Assert.Equal(2, causes.InnerExceptions.Count);
        }
        finally
        {
            Docker.Start(Cluster.FirstReplicaContainer);
            Docker.Start(Cluster.SecondReplicaContainer);
            await WaitUntilReachableAsync(Cluster.FirstReplicaConnectionString, TimeSpan.FromSeconds(60));
            await WaitUntilReachableAsync(Cluster.SecondReplicaConnectionString, TimeSpan.FromSeconds(60));
        }
    }

    [DockerFact]
    public async Task A_recovered_replica_is_used_again_once_its_cooldown_expires()
    {
        await using var provider = Build(options => options.ReplicaFailureCooldown = TimeSpan.FromSeconds(1), withRetry: true);

        Docker.Stop(Cluster.FirstReplicaContainer);
        try
        {
            // Mark it down.
            Assert.Equal(LiveClusterFixture.SecondReplicaSource, await SourceAsync(provider));
        }
        finally
        {
            Docker.Start(Cluster.FirstReplicaContainer);
            await WaitUntilReachableAsync(Cluster.FirstReplicaConnectionString, TimeSpan.FromSeconds(60));
        }

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var seen = await SourcesAsync(provider, 10);
        Assert.Contains(LiveClusterFixture.FirstReplicaSource, seen);
    }

    [DockerFact]
    public async Task SaveChanges_reaches_the_master_from_inside_a_replica_scope()
    {
        await using var provider = Build();
        var target = provider.GetRequiredService<IDbTargetContext>();
        var written = $"scoped-{Guid.NewGuid():N}";

        using (target.UseReplicaDb())
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
            db.Markers.Add(new Marker { Source = written });
            await db.SaveChangesAsync();

            // Stickiness: after the write, this scope reads from the master.
            Assert.Equal(DbTarget.Master, target.CurrentTarget);
        }

        Assert.Contains(written, await ReadAllAsync(Cluster.MasterConnectionString));
    }

    private async Task<string[]> ReadAllAsync(string connectionString)
    {
        await using var connection = Cluster.CreateConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = ReadAllSql;

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows.ToArray();
    }

    protected abstract string ReadAllSql { get; }
}

public sealed class PostgreSqlLiveTests(PostgreSqlCluster cluster) : LiveRoutingTests<PostgreSqlCluster>(cluster)
{
    protected override string ReadAllSql => "SELECT \"Source\" FROM \"Markers\" ORDER BY \"Id\"";
}

public sealed class MySqlLiveTests(MySqlCluster cluster) : LiveRoutingTests<MySqlCluster>(cluster)
{
    protected override string ReadAllSql => "SELECT `Source` FROM `Markers` ORDER BY `Id`";
}

public sealed class SqlServerLiveTests(SqlServerCluster cluster) : LiveRoutingTests<SqlServerCluster>(cluster)
{
    protected override string ReadAllSql => "SELECT [Source] FROM [Markers] ORDER BY [Id]";
}

public sealed class OracleLiveTests(OracleCluster cluster) : LiveRoutingTests<OracleCluster>(cluster)
{
    protected override string ReadAllSql => "SELECT \"Source\" FROM \"Markers\" ORDER BY \"Id\"";
}
