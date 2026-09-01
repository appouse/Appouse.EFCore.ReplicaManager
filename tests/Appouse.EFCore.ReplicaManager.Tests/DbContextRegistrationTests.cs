using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// What each DbContext registration style actually does. These exist because the difference is
/// invisible at compile time: forgetting to wire the interceptors is a silent no-op, not an error.
/// </summary>
public sealed class DbContextRegistrationTests : IClassFixture<TwoDatabaseFixture>
{
    private readonly TwoDatabaseFixture _fx;

    public DbContextRegistrationTests(TwoDatabaseFixture fx) => _fx = fx;

    private IServiceCollection Splitting()
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
            options.DefaultTarget = DbTarget.Replica;
        });
        return services;
    }

    private static async Task<string> SourceAsync(MarkerContext db)
        => await db.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync();

    [Fact]
    public async Task Plain_AddDbContext_without_the_interceptors_is_a_silent_no_op()
    {
        var services = Splitting();

        // No UseMasterReplicaSplitting(sp) here - the package's services are registered, but nothing
        // ever reaches this DbContext.
        services.AddDbContext<MarkerContext>(options => options.UseSqlite(_fx.MasterConnectionString));

        await using var provider = services.BuildServiceProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        // DefaultTarget is ReadReplica and we are inside an explicit read scope, yet the query
        // still goes wherever UseSqlite pointed. No exception, no warning.
        using (target.UseReplicaDb())
        {
            Assert.Equal("master", await SourceAsync(db));
        }
    }

    [Fact]
    public async Task Plain_AddDbContext_plus_UseReadWriteSplitting_routes_normally()
    {
        var services = Splitting();

        services.AddDbContext<MarkerContext>((sp, options) =>
        {
            options.UseSqlite(_fx.MasterConnectionString);
            options.UseMasterReplicaSplitting(sp);
        });

        await using var provider = services.BuildServiceProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        Assert.Equal("replica", await SourceAsync(db));

        using (target.UseMasterDb())
        {
            Assert.Equal("master", await SourceAsync(db));
        }
    }

    [Fact]
    public async Task AddDbContextFactory_plus_UseReadWriteSplitting_routes_normally()
    {
        var services = Splitting();

        services.AddDbContextFactory<MarkerContext>((sp, options) =>
        {
            options.UseSqlite(_fx.MasterConnectionString);
            options.UseMasterReplicaSplitting(sp);
        });

        await using var provider = services.BuildServiceProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();
        var factory = provider.GetRequiredService<IDbContextFactory<MarkerContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal("replica", await SourceAsync(db));
        }

        using (target.UseMasterDb())
        {
            await using var db = await factory.CreateDbContextAsync();
            Assert.Equal("master", await SourceAsync(db));
        }
    }

    [Fact]
    public async Task A_factory_context_follows_the_flow_that_uses_it_not_the_one_that_made_it()
    {
        var services = Splitting();

        services.AddDbContextFactory<MarkerContext>((sp, options) =>
        {
            options.UseSqlite(_fx.MasterConnectionString);
            options.UseMasterReplicaSplitting(sp);
        });

        await using var provider = services.BuildServiceProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();
        var factory = provider.GetRequiredService<IDbContextFactory<MarkerContext>>();

        // Created under a write scope...
        MarkerContext db;
        using (target.UseMasterDb())
        {
            db = await factory.CreateDbContextAsync();
        }

        // ...but queried under a read scope: routing happens when the connection opens, so the
        // scope that runs the query wins.
        await using (db)
        {
            using (target.UseReplicaDb())
            {
                Assert.Equal("replica", await SourceAsync(db));
            }
        }
    }

    [Fact]
    public async Task AddReadWriteDbContextPool_routes_normally()
    {
        var services = Splitting();
        services.AddMasterReplicaDbContextPool<MarkerContext>((options, cs) => options.UseSqlite(cs));

        await using var provider = services.BuildServiceProvider();
        var target = provider.GetRequiredService<IDbTargetContext>();

        for (var i = 0; i < 4; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

            // Pooled instances are reused, so this also proves no routing state sticks to a context.
            Assert.Equal("replica", await SourceAsync(db));

            using (target.UseMasterDb())
            {
                Assert.Equal("master", await SourceAsync(db));
            }
        }
    }
}
