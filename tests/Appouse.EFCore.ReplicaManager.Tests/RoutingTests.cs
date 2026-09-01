using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// End-to-end routing tests. Every assertion reads a row back out of a real database, so it proves
/// which physical file the query actually reached rather than what the package intended.
/// </summary>
public sealed class RoutingTests : IClassFixture<TwoDatabaseFixture>
{
    private readonly TwoDatabaseFixture _fx;

    public RoutingTests(TwoDatabaseFixture fx) => _fx = fx;

    private ServiceProvider BuildProvider(Action<MasterReplicaOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
            configure?.Invoke(options);
        });
        services.AddMasterReplicaDbContext<MarkerContext>((options, connectionString) => options.UseSqlite(connectionString));
        return services.BuildServiceProvider();
    }

    private static async Task<string> ReadSourceAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
        return await db.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync();
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

    [Fact]
    public async Task Default_target_is_the_master()
    {
        await using var provider = BuildProvider();
        Assert.Equal("master", await ReadSourceAsync(provider));
    }

    [Fact]
    public async Task DefaultTarget_option_routes_to_the_replica()
    {
        await using var provider = BuildProvider(o => o.DefaultTarget = DbTarget.Replica);
        Assert.Equal("replica", await ReadSourceAsync(provider));
    }

    [Fact]
    public async Task UseTarget_scope_switches_the_physical_database()
    {
        await using var provider = BuildProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();

        Assert.Equal("master", await ReadSourceAsync(provider));

        using (target.UseTarget(DbTarget.Replica))
        {
            Assert.Equal("replica", await ReadSourceAsync(provider));
        }

        Assert.Equal("master", await ReadSourceAsync(provider));
    }

    [Fact]
    public async Task UseTarget_scopes_nest_and_unwind_in_order()
    {
        await using var provider = BuildProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();

        using (target.UseReplicaDb())
        {
            Assert.Equal("replica", await ReadSourceAsync(provider));

            using (target.UseMasterDb())
            {
                Assert.Equal("master", await ReadSourceAsync(provider));

                using (target.UseReplicaDb())
                {
                    Assert.Equal("replica", await ReadSourceAsync(provider));
                }

                Assert.Equal("master", await ReadSourceAsync(provider));
            }

            Assert.Equal("replica", await ReadSourceAsync(provider));
        }

        Assert.Equal("master", await ReadSourceAsync(provider));
    }

    [Fact]
    public void Disposing_a_scope_twice_is_a_no_op()
    {
        using var provider = BuildProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();

        var outer = target.UseTarget(DbTarget.Replica);
        var inner = target.UseTarget(DbTarget.Master);

        inner.Dispose();
        inner.Dispose();
        inner.Dispose();

        Assert.Equal(DbTarget.Replica, target.CurrentTarget);

        outer.Dispose();
        Assert.Equal(DbTarget.Master, target.CurrentTarget);
    }

    [Fact]
    public async Task Concurrent_flows_do_not_leak_their_target_into_each_other()
    {
        await using var provider = BuildProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();

        async Task<string> ReadUnder(DbTarget wanted, int delayMs)
        {
            using (target.UseTarget(wanted))
            {
                await Task.Delay(delayMs);
                return await ReadSourceAsync(provider);
            }
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 24).Select(i =>
                i % 2 == 0 ? ReadUnder(DbTarget.Replica, 5 + (i % 7)) : ReadUnder(DbTarget.Master, 5 + (i % 5))));

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(i % 2 == 0 ? "replica" : "master", results[i]);
        }

        Assert.Equal(DbTarget.Master, target.CurrentTarget);
    }

    [Fact]
    public async Task SaveChanges_is_forced_onto_the_master_even_inside_a_replica_scope()
    {
        await using var provider = BuildProvider(o => o.DefaultTarget = DbTarget.Replica);
        var target = provider.GetRequiredService<IDbTargetContext>();
        var written = $"written-{Guid.NewGuid():N}";

        using (target.UseTarget(DbTarget.Replica))
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
            db.Markers.Add(new Marker { Source = written });
            await db.SaveChangesAsync();
        }

        Assert.Contains(written, await ReadAllAsync(_fx.MasterConnectionString));
        Assert.DoesNotContain(written, await ReadAllAsync(_fx.ReplicaConnectionString));
    }

    [Fact]
    public async Task Reads_after_a_write_stick_to_the_master_for_the_rest_of_the_scope()
    {
        await using var provider = BuildProvider(o => o.DefaultTarget = DbTarget.Replica);
        var target = provider.GetRequiredService<IDbTargetContext>();

        using (target.UseTarget(DbTarget.Replica))
        {
            Assert.Equal("replica", await ReadSourceAsync(provider));

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
                db.Markers.Add(new Marker { Source = $"sticky-{Guid.NewGuid():N}" });
                await db.SaveChangesAsync();
            }

            // The write pinned the master, and the pin is visible to this caller because the
            // ambient holder is mutated in place rather than reassigned.
            Assert.Equal(DbTarget.Master, target.CurrentTarget);
            Assert.Equal("master", await ReadSourceAsync(provider));
        }

        // ...and the pin dies with the scope.
        Assert.Equal(DbTarget.Replica, target.CurrentTarget);
    }

    [Fact]
    public async Task Stickiness_can_be_turned_off()
    {
        await using var provider = BuildProvider(o =>
        {
            o.DefaultTarget = DbTarget.Replica;
            o.StickToMasterAfterSaveChanges = false;
        });
        var target = provider.GetRequiredService<IDbTargetContext>();

        using (target.UseTarget(DbTarget.Replica))
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
                db.Markers.Add(new Marker { Source = $"nonsticky-{Guid.NewGuid():N}" });
                await db.SaveChangesAsync();
            }

            Assert.Equal(DbTarget.Replica, target.CurrentTarget);
            Assert.Equal("replica", await ReadSourceAsync(provider));
        }
    }

    [Fact]
    public async Task An_explicit_transaction_started_inside_a_write_scope_reaches_the_master()
    {
        await using var provider = BuildProvider(o => o.DefaultTarget = DbTarget.Replica);
        var target = provider.GetRequiredService<IDbTargetContext>();
        var written = $"tx-{Guid.NewGuid():N}";

        using (target.UseMasterDb())
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Markers.Add(new Marker { Source = written });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        Assert.Contains(written, await ReadAllAsync(_fx.MasterConnectionString));
    }

    [Fact]
    public async Task Multiple_replicas_are_used_round_robin()
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
            options.ReplicaConnectionStrings.Add(_fx.SecondReplicaConnectionString);
            options.DefaultTarget = DbTarget.Replica;
        });
        services.AddMasterReplicaDbContext<MarkerContext>((options, connectionString) => options.UseSqlite(connectionString));

        await using var provider = services.BuildServiceProvider();

        var seen = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            seen.Add(await ReadSourceAsync(provider));
        }

        Assert.Contains("replica", seen);
        Assert.Contains("replica-2", seen);
        Assert.DoesNotContain("master", seen);
    }

    [Fact]
    public void Misconfiguration_stops_the_host_at_start_up()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = string.Empty;
            options.ReplicaConnectionString = string.Empty;
        });

        using var host = builder.Build();

        var error = Assert.Throws<OptionsValidationException>(() => host.Start());
        Assert.Contains(nameof(MasterReplicaOptions.MasterConnectionString), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MasterReplicaOptions.ReplicaConnectionString), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undefined_target_is_rejected()
    {
        using var provider = BuildProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();

        Assert.Throws<ArgumentOutOfRangeException>(() => target.UseTarget((DbTarget)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => target.SetTarget((DbTarget)99));
    }
}
