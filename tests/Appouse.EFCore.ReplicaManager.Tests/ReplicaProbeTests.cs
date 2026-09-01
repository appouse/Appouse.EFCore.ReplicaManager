using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// Proving a replica is really there, rather than merely that a connection object was handed back.
/// </summary>
public sealed class ReplicaProbeTests : IClassFixture<TwoDatabaseFixture>
{
    private readonly TwoDatabaseFixture _fx;

    public ReplicaProbeTests(TwoDatabaseFixture fx) => _fx = fx;

    private ServiceProvider Build(Action<MasterReplicaOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.DefaultTarget = DbTarget.Replica;
            configure(options);
        });
        services.AddMasterReplicaDbContext<MarkerContext>((o, cs) => o.UseSqlite(cs));
        return services.BuildServiceProvider();
    }

    private static MarkerContext Context(IServiceProvider provider, out IServiceScope scope)
    {
        scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<MarkerContext>();
    }

    [Fact]
    public async Task Healthy_replicas_all_report_reachable()
    {
        await using var provider = Build(o =>
        {
            o.ReplicaConnectionString = _fx.ReplicaConnectionString;
            o.ReplicaConnectionStrings.Add(_fx.SecondReplicaConnectionString);
        });

        var db = Context(provider, out var scope);
        using (scope)
        {
            var results = await db.Database.ProbeReplicasAsync();

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.IsReachable));
            Assert.All(results, r => Assert.Null(r.Error));
            Assert.All(results, r => Assert.True(r.Duration > TimeSpan.Zero));
            Assert.Equal(new[] { 0, 1 }, results.Select(r => r.ReplicaIndex));
        }
    }

    [Fact]
    public async Task An_unreachable_replica_is_reported_with_its_reason()
    {
        await using var provider = Build(o =>
        {
            o.ReplicaConnectionString = _fx.ReplicaConnectionString;
            o.ReplicaConnectionStrings.Add(TwoDatabaseFixture.UnreachableConnectionString);
        });

        var db = Context(provider, out var scope);
        using (scope)
        {
            var results = await db.Database.ProbeReplicasAsync();

            Assert.True(results[0].IsReachable);
            Assert.False(results[1].IsReachable);
            Assert.False(string.IsNullOrWhiteSpace(results[1].Error));
        }
    }

    [Fact]
    public async Task A_probe_feeds_the_health_monitor()
    {
        await using var provider = Build(o =>
        {
            o.ReplicaConnectionString = _fx.ReplicaConnectionString;
            o.ReplicaConnectionStrings.Add(TwoDatabaseFixture.UnreachableConnectionString);
            o.ReplicaFailureCooldown = TimeSpan.FromMinutes(5);
        });

        var health = provider.GetRequiredService<IReplicaHealthMonitor>();
        Assert.True(health.IsAvailable(1));

        var db = Context(provider, out var scope);
        using (scope)
        {
            await db.Database.ProbeReplicasAsync();
        }

        // The probe found it down, so routing now avoids it without waiting for a request to fail.
        Assert.True(health.IsAvailable(0));
        Assert.False(health.IsAvailable(1));
    }

    /// <summary>
    /// Options validation forbids a topology with no replica, so this path is only reachable through
    /// a custom resolver - which is exactly how a multi-tenant application might report that a
    /// particular tenant has none.
    /// </summary>
    [Fact]
    public async Task Probing_reports_nothing_when_the_resolver_offers_no_replica()
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(o =>
        {
            o.MasterConnectionString = _fx.MasterConnectionString;
            o.ReplicaConnectionString = _fx.ReplicaConnectionString;
            o.DefaultTarget = DbTarget.Master;
        });
        services.Replace(ServiceDescriptor.Singleton<IDbConnectionStringResolver>(
            new MasterOnlyResolver(_fx.MasterConnectionString)));
        services.AddMasterReplicaDbContext<MarkerContext>((o, cs) => o.UseSqlite(cs));

        await using var provider = services.BuildServiceProvider();
        var db = Context(provider, out var scope);
        using (scope)
        {
            Assert.Empty(await db.Database.ProbeReplicasAsync());
        }
    }

    private sealed class MasterOnlyResolver(string master) : IDbConnectionStringResolver
    {
        public string GetMasterConnectionString() => master;

        public IReadOnlyList<string> GetReplicaConnectionStrings() => Array.Empty<string>();
    }

    [Fact]
    public async Task Probing_an_unrouted_context_explains_why_it_cannot()
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(o =>
        {
            o.MasterConnectionString = _fx.MasterConnectionString;
            o.ReplicaConnectionString = _fx.ReplicaConnectionString;
            o.ValidateStartupWiring = false;
        });
        services.AddDbContext<MarkerContext>(o => o.UseSqlite(_fx.MasterConnectionString));

        await using var provider = services.BuildServiceProvider();
        var db = Context(provider, out var scope);
        using (scope)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.Database.ProbeReplicasAsync());
            Assert.Contains("AddMasterReplicaDbContext", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Validation_makes_a_dead_replica_fail_over_at_open_time()
    {
        await using var provider = Build(o =>
        {
            o.ReplicaConnectionString = TwoDatabaseFixture.UnreachableConnectionString;
            o.ReplicaConnectionStrings.Add(_fx.SecondReplicaConnectionString);
            o.ValidateReplicaConnections = true;
        });

        var db = Context(provider, out var scope);
        using (scope)
        {
            Assert.Equal("replica-2", await db.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync());
        }
    }

    [Fact]
    public async Task Validation_does_not_disturb_a_healthy_replica()
    {
        await using var provider = Build(o =>
        {
            o.ReplicaConnectionString = _fx.ReplicaConnectionString;
            o.ValidateReplicaConnections = true;
        });

        for (var i = 0; i < 4; i++)
        {
            var db = Context(provider, out var scope);
            using (scope)
            {
                Assert.Equal("replica", await db.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync());
            }
        }
    }
}
