using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// One master, many replicas: what happens when a replica stops answering. Every assertion reads a
/// row back, so it proves which physical database actually served the query.
/// </summary>
public sealed class FailoverTests : IClassFixture<TwoDatabaseFixture>
{
    private readonly TwoDatabaseFixture _fx;

    public FailoverTests(TwoDatabaseFixture fx) => _fx = fx;

    private ServiceProvider Build(Action<MasterReplicaOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.DefaultTarget = DbTarget.Replica;
            configure(options);
        });
        services.AddMasterReplicaDbContext<MarkerContext>((options, cs) => options.UseSqlite(cs));
        return services.BuildServiceProvider();
    }

    private static async Task<string> SourceAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
        return await db.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync();
    }

    [Fact]
    public async Task A_dead_replica_fails_over_to_the_next_one()
    {
        await using var provider = Build(options =>
        {
            options.ReplicaConnectionString = TwoDatabaseFixture.UnreachableConnectionString;
            options.ReplicaConnectionStrings.Add(_fx.SecondReplicaConnectionString);
        });

        Assert.Equal("replica-2", await SourceAsync(provider));
    }

    [Fact]
    public async Task The_dead_replica_is_skipped_on_subsequent_connections()
    {
        await using var provider = Build(options =>
        {
            options.ReplicaConnectionString = TwoDatabaseFixture.UnreachableConnectionString;
            options.ReplicaConnectionStrings.Add(_fx.SecondReplicaConnectionString);
        });

        // Round-robin alone would send every other connection back to the dead replica. The health
        // monitor puts it at the back of the queue, so the live one answers every time.
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal("replica-2", await SourceAsync(provider));
        }
    }

    [Fact]
    public async Task Failover_walks_past_several_dead_replicas()
    {
        await using var provider = Build(options =>
        {
            options.ReplicaConnectionString = TwoDatabaseFixture.UnreachableConnectionString;
            options.ReplicaConnectionStrings.Add(TwoDatabaseFixture.UnreachableConnectionString);
            options.ReplicaConnectionStrings.Add(TwoDatabaseFixture.UnreachableConnectionString);
            options.ReplicaConnectionStrings.Add(_fx.ReplicaConnectionString);
        });

        Assert.Equal("replica", await SourceAsync(provider));
    }

    [Fact]
    public async Task When_every_replica_is_down_the_master_serves_the_read()
    {
        await using var provider = Build(options =>
        {
            options.ReplicaConnectionString = TwoDatabaseFixture.UnreachableConnectionString;
            options.ReplicaConnectionStrings.Add(TwoDatabaseFixture.UnreachableConnectionString);
            options.AllowReplicaFallbackToMaster = true;
        });

        Assert.Equal("master", await SourceAsync(provider));
    }

    [Fact]
    public async Task When_every_replica_is_down_and_fallback_is_disabled_it_throws()
    {
        await using var provider = Build(options =>
        {
            options.ReplicaConnectionString = TwoDatabaseFixture.UnreachableConnectionString;
            options.ReplicaConnectionStrings.Add(TwoDatabaseFixture.UnreachableConnectionString);
            options.AllowReplicaFallbackToMaster = false;
        });

        var error = await Assert.ThrowsAsync<ReplicaUnavailableException>(() => SourceAsync(provider));

        var causes = Assert.IsType<AggregateException>(error.InnerException);
        Assert.Equal(2, causes.InnerExceptions.Count);
        Assert.All(causes.InnerExceptions, e => Assert.IsType<SqliteException>(e));
    }

    [Fact]
    public async Task A_read_still_reaches_a_live_replica_while_another_is_down()
    {
        await using var provider = Build(options =>
        {
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
            options.ReplicaConnectionStrings.Add(TwoDatabaseFixture.UnreachableConnectionString);
            options.ReplicaConnectionStrings.Add(_fx.SecondReplicaConnectionString);
        });

        var seen = new List<string>();
        for (var i = 0; i < 9; i++)
        {
            seen.Add(await SourceAsync(provider));
        }

        Assert.DoesNotContain("master", seen);
        Assert.Contains("replica", seen);
        Assert.Contains("replica-2", seen);
    }

    [Fact]
    public async Task Writes_are_never_failed_over_because_there_is_only_one_master()
    {
        await using var provider = Build(options =>
        {
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
        });

        var target = provider.GetRequiredService<IDbTargetContext>();
        var written = $"failover-{Guid.NewGuid():N}";

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
            db.Markers.Add(new Marker { Source = written });
            await db.SaveChangesAsync();
        }

        using (target.UseMasterDb())
        {
            var rows = await ReadAllAsync(_fx.MasterConnectionString);
            Assert.Contains(written, rows);
        }
    }

    private static async Task<string[]> ReadAllAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Source FROM Markers ORDER BY Id;";

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows.ToArray();
    }
}

/// <summary>
/// The cooldown logic on its own, without a database in the way.
/// </summary>
public sealed class ReplicaHealthMonitorTests
{
    private static ReplicaHealthMonitor Build(int replicaCount, TimeSpan cooldown)
    {
        var options = new MasterReplicaOptions
        {
            MasterConnectionString = "master",
            ReplicaConnectionString = "replica-0",
            ReplicaFailureCooldown = cooldown,
        };

        for (var i = 1; i < replicaCount; i++)
        {
            options.ReplicaConnectionStrings.Add($"replica-{i}");
        }

        var wrapped = Options.Create(options);
        return new ReplicaHealthMonitor(new DbConnectionStringResolver(wrapped), wrapped);
    }

    [Fact]
    public void A_replica_starts_available()
        => Assert.True(Build(3, TimeSpan.FromSeconds(30)).IsAvailable(0));

    [Fact]
    public void A_failure_makes_it_unavailable_for_the_cooldown()
    {
        var monitor = Build(3, TimeSpan.FromMinutes(5));
        monitor.ReportFailure(1, new InvalidOperationException("down"));

        Assert.False(monitor.IsAvailable(1));
        Assert.True(monitor.IsAvailable(0));
        Assert.True(monitor.IsAvailable(2));
    }

    [Fact]
    public void A_success_clears_the_cooldown()
    {
        var monitor = Build(2, TimeSpan.FromMinutes(5));
        monitor.ReportFailure(0, new InvalidOperationException("down"));
        Assert.False(monitor.IsAvailable(0));

        monitor.ReportSuccess(0);
        Assert.True(monitor.IsAvailable(0));
    }

    [Fact]
    public void A_zero_cooldown_disables_the_skip_entirely()
    {
        var monitor = Build(2, TimeSpan.Zero);
        monitor.ReportFailure(0, new InvalidOperationException("down"));

        Assert.True(monitor.IsAvailable(0));
    }

    [Fact]
    public void An_out_of_range_index_is_treated_as_available_rather_than_throwing()
    {
        var monitor = Build(2, TimeSpan.FromMinutes(5));

        Assert.True(monitor.IsAvailable(99));
        monitor.ReportFailure(99, new InvalidOperationException("down"));
        monitor.ReportSuccess(99);
    }
}
