using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// What happens when code steps outside EF Core and talks to the connection directly - Dapper, or
/// plain ADO.NET. The routing interceptor only fires on the paths EF Core itself opens, so the
/// boundary matters and is pinned down here rather than left to be discovered in production.
/// </summary>
public sealed class DapperAndRawAdoTests : IClassFixture<TwoDatabaseFixture>
{
    private readonly TwoDatabaseFixture _fx;

    public DapperAndRawAdoTests(TwoDatabaseFixture fx) => _fx = fx;

    private ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
            options.DefaultTarget = DbTarget.Master;
        });
        services.AddMasterReplicaDbContext<MarkerContext>((o, cs) => o.UseSqlite(cs));
        return services.BuildServiceProvider();
    }

    private const string Sql = "SELECT Source FROM Markers ORDER BY Id LIMIT 1";

    /// <summary>
    /// Taking the raw connection and opening it yourself bypasses EF Core's own open, so the
    /// interceptor never runs and the connection keeps whatever string it already had - here, the
    /// one the provider was configured with, which is the master.
    /// </summary>
    [Fact]
    public async Task GetDbConnection_then_Open_is_NOT_routed()
    {
        await using var provider = Build();
        var target = provider.GetRequiredService<IDbTargetContext>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        using (target.UseReplicaDb())
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            try
            {
                var source = await connection.QuerySingleAsync<string>(Sql);

                // The ambient target says replica, but the query went to the master.
                Assert.Equal("master", source);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Opening through the DbContext goes through EF Core's connection pipeline, so the interceptor
    /// fires and Dapper then runs against the routed connection.
    /// </summary>
    [Fact]
    public async Task Database_OpenConnection_IS_routed()
    {
        await using var provider = Build();
        var target = provider.GetRequiredService<IDbTargetContext>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        using (target.UseReplicaDb())
        {
            await db.Database.OpenConnectionAsync();
            try
            {
                var source = await db.Database.GetDbConnection().QuerySingleAsync<string>(Sql);
                Assert.Equal("replica", source);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>
    /// Dapper opens a closed connection itself, and that open is a plain ADO.NET call, so it is not
    /// routed either.
    /// </summary>
    [Fact]
    public async Task Dapper_opening_the_connection_itself_is_NOT_routed()
    {
        await using var provider = Build();
        var target = provider.GetRequiredService<IDbTargetContext>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        using (target.UseReplicaDb())
        {
            var connection = db.Database.GetDbConnection();
            Assert.Equal(ConnectionState.Closed, connection.State);

            var source = await connection.QuerySingleAsync<string>(Sql);
            Assert.Equal("master", source);
        }
    }

    /// <summary>
    /// The sharp edge. EF Core leaves the connection string it last used on the connection object,
    /// so raw access after an EF query inherits that route rather than the ambient target - a write
    /// issued this way would reach a read-only replica.
    /// </summary>
    [Fact]
    public async Task Raw_access_after_an_EF_query_inherits_the_previous_route()
    {
        await using var provider = Build();
        var target = provider.GetRequiredService<IDbTargetContext>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        // An ordinary EF read routed to the replica; EF closes the connection afterwards but leaves
        // the replica connection string on it.
        using (target.UseReplicaDb())
        {
            Assert.Equal("replica", await db.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync());
        }

        // Now the ambient target is the master - and raw access still lands on the replica.
        Assert.Equal(DbTarget.Master, target.CurrentTarget);

        var connection = db.Database.GetDbConnection();
        var source = await connection.QuerySingleAsync<string>(Sql);

        Assert.Equal("replica", source);
    }

    /// <summary>
    /// The helper hands Dapper a connection EF Core opened, so it carries the ambient target.
    /// </summary>
    [Fact]
    public async Task OpenRoutedConnectionAsync_follows_the_ambient_target()
    {
        await using var provider = Build();
        var target = provider.GetRequiredService<IDbTargetContext>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        using (target.UseReplicaDb())
        {
            await using var routed = await db.Database.OpenRoutedConnectionAsync();
            Assert.Equal("replica", await routed.Connection.QuerySingleAsync<string>(Sql));
        }

        using (target.UseMasterDb())
        {
            await using var routed = await db.Database.OpenRoutedConnectionAsync();
            Assert.Equal("master", await routed.Connection.QuerySingleAsync<string>(Sql));
        }
    }

    /// <summary>
    /// The whole point: the helper does not inherit the route left behind by an earlier EF query.
    /// </summary>
    [Fact]
    public async Task OpenRoutedConnectionAsync_does_not_inherit_a_stale_route()
    {
        await using var provider = Build();
        var target = provider.GetRequiredService<IDbTargetContext>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        using (target.UseReplicaDb())
        {
            Assert.Equal("replica", await db.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync());
        }

        // Raw access here would still hit the replica; the helper re-routes.
        await using var routed = await db.Database.OpenRoutedConnectionAsync();
        Assert.Equal("master", await routed.Connection.QuerySingleAsync<string>(Sql));
    }

    [Fact]
    public async Task Disposing_the_handle_returns_the_connection_to_the_context()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        await using (var routed = await db.Database.OpenRoutedConnectionAsync())
        {
            Assert.Equal(ConnectionState.Open, routed.Connection.State);
        }

        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task Disposing_the_handle_twice_is_a_no_op()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        var routed = await db.Database.OpenRoutedConnectionAsync();
        await routed.DisposeAsync();
        await routed.DisposeAsync();
        routed.Dispose();

        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public void The_synchronous_helper_routes_too()
    {
        using var provider = Build();
        var target = provider.GetRequiredService<IDbTargetContext>();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        using (target.UseReplicaDb())
        {
            using var routed = db.Database.OpenRoutedConnection();
            Assert.Equal("replica", routed.Connection.QuerySingle<string>(Sql));
        }
    }
}
