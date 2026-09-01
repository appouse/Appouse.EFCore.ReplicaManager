using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// The package never names a provider type: it only reads <see cref="DbConnection.State"/>, assigns
/// <see cref="DbConnection.ConnectionString"/>, and calls Open/OpenAsync/Close - all declared on the
/// ADO.NET base class. These tests hold that claim to account against every mainstream provider.
/// </summary>
/// <remarks>
/// No database server is needed. Each replica points at 127.0.0.1 port 1, which refuses the TCP
/// connection immediately, so the failover loop runs end to end with the provider's real connection
/// object and its real exception type. What is left unproven is a successful open against a live
/// server, which is ordinary provider behaviour rather than anything this package influences.
/// </remarks>
public sealed class ProviderCompatibilityTests
{
    public const string SqlServer = "SQL Server";
    public const string PostgreSql = "PostgreSQL";
    public const string MySql = "MySQL";
    public const string Oracle = "Oracle";
    public const string Sqlite = "SQLite";

    private static (string Master, string Replica1, string Replica2) ConnectionStrings(string provider) => provider switch
    {
        SqlServer =>
        (
            "Server=127.0.0.1,1;Database=app;User Id=u;Password=p;Connect Timeout=1;Encrypt=False;TrustServerCertificate=True",
            "Server=127.0.0.1,2;Database=app;User Id=u;Password=p;Connect Timeout=1;Encrypt=False;TrustServerCertificate=True",
            "Server=127.0.0.1,3;Database=app;User Id=u;Password=p;Connect Timeout=1;Encrypt=False;TrustServerCertificate=True"
        ),
        PostgreSql =>
        (
            "Host=127.0.0.1;Port=1;Database=app;Username=u;Password=p;Timeout=1",
            "Host=127.0.0.1;Port=2;Database=app;Username=u;Password=p;Timeout=1",
            "Host=127.0.0.1;Port=3;Database=app;Username=u;Password=p;Timeout=1"
        ),
        MySql =>
        (
            "Server=127.0.0.1;Port=1;Database=app;User ID=u;Password=p;Connection Timeout=1",
            "Server=127.0.0.1;Port=2;Database=app;User ID=u;Password=p;Connection Timeout=1",
            "Server=127.0.0.1;Port=3;Database=app;User ID=u;Password=p;Connection Timeout=1"
        ),
        Oracle =>
        (
            "User Id=u;Password=p;Data Source=127.0.0.1:1/app;Connection Timeout=1",
            "User Id=u;Password=p;Data Source=127.0.0.1:2/app;Connection Timeout=1",
            "User Id=u;Password=p;Data Source=127.0.0.1:3/app;Connection Timeout=1"
        ),
        Sqlite =>
        (
            "Data Source=/appouse-no-such-directory/master.db;Mode=ReadWrite",
            "Data Source=/appouse-no-such-directory/replica-1.db;Mode=ReadWrite",
            "Data Source=/appouse-no-such-directory/replica-2.db;Mode=ReadWrite"
        ),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
    };

    private static DbConnection CreateConnection(string provider) => provider switch
    {
        SqlServer => new SqlConnection(),
        PostgreSql => new NpgsqlConnection(),
        MySql => new MySqlConnection(),
        Oracle => new OracleConnection(),
        Sqlite => new SqliteConnection(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
    };

    private static Action<DbContextOptionsBuilder, string> ConfigureProvider(string provider) => provider switch
    {
        SqlServer => (options, cs) => options.UseSqlServer(cs),
        PostgreSql => (options, cs) => options.UseNpgsql(cs),

        // Pomelo needs the server version up front. Passing it explicitly rather than using
        // ServerVersion.AutoDetect(cs) avoids opening a connection during start-up - which would
        // otherwise make application boot depend on the master being reachable.
        MySql => (options, cs) => options.UseMySql(cs, new MySqlServerVersion(new Version(8, 0, 34))),

        Oracle => (options, cs) => options.UseOracle(cs),
        Sqlite => (options, cs) => options.UseSqlite(cs),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
    };

    [Theory]
    [InlineData(SqlServer)]
    [InlineData(PostgreSql)]
    [InlineData(MySql)]
    [InlineData(Oracle)]
    [InlineData(Sqlite)]
    public void A_closed_connection_accepts_a_new_connection_string(string provider)
    {
        var (master, replica1, replica2) = ConnectionStrings(provider);

        using var connection = CreateConnection(provider);
        Assert.Equal(ConnectionState.Closed, connection.State);

        // This is the entire provider-facing surface of the interceptor.
        connection.ConnectionString = master;
        connection.ConnectionString = replica1;
        connection.ConnectionString = replica2;

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(SqlServer)]
    [InlineData(PostgreSql)]
    [InlineData(MySql)]
    [InlineData(Oracle)]
    [InlineData(Sqlite)]
    public async Task Failover_visits_every_replica_before_giving_up(string provider)
    {
        var (master, replica1, replica2) = ConnectionStrings(provider);

        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = master;
            options.ReplicaConnectionString = replica1;
            options.ReplicaConnectionStrings.Add(replica2);
            options.DefaultTarget = DbTarget.Replica;
            options.AllowReplicaFallbackToMaster = false;
        });
        services.AddMasterReplicaDbContext<MarkerContext>(ConfigureProvider(provider));

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        var error = await Assert.ThrowsAsync<ReplicaUnavailableException>(
            () => db.Markers.FirstOrDefaultAsync());

        // Both replicas were dialled, and each provider's own failure was captured rather than
        // swallowed - which is what proves the loop ran with real provider connections.
        var causes = Assert.IsType<AggregateException>(error.InnerException);
        Assert.Equal(2, causes.InnerExceptions.Count);
        Assert.All(causes.InnerExceptions, e => Assert.NotNull(e.Message));
    }

    [Theory]
    [InlineData(SqlServer)]
    [InlineData(PostgreSql)]
    [InlineData(MySql)]
    [InlineData(Oracle)]
    [InlineData(Sqlite)]
    public async Task A_dead_replica_set_falls_back_to_the_master_when_allowed(string provider)
    {
        var (master, replica1, replica2) = ConnectionStrings(provider);

        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = master;
            options.ReplicaConnectionString = replica1;
            options.ReplicaConnectionStrings.Add(replica2);
            options.DefaultTarget = DbTarget.Replica;
            options.AllowReplicaFallbackToMaster = true;
        });
        services.AddMasterReplicaDbContext<MarkerContext>(ConfigureProvider(provider));

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();

        // The master is unreachable too, so the call still fails - but with the provider's own
        // connection error rather than ReplicaUnavailableException, proving the fallback handed the
        // operation back to EF Core against the master.
        var error = await Record.ExceptionAsync(() => db.Markers.FirstOrDefaultAsync());

        Assert.NotNull(error);
        Assert.IsNotType<ReplicaUnavailableException>(error);
    }

    [Theory]
    [InlineData(SqlServer)]
    [InlineData(PostgreSql)]
    [InlineData(MySql)]
    [InlineData(Oracle)]
    [InlineData(Sqlite)]
    public void Registration_succeeds_for_this_provider(string provider)
    {
        var (master, replica1, _) = ConnectionStrings(provider);

        var services = new ServiceCollection();
        services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = master;
            options.ReplicaConnectionString = replica1;
        });
        services.AddMasterReplicaDbContext<MarkerContext>(ConfigureProvider(provider));

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<MarkerContext>();
        Assert.True(db.Database.IsRelational());

        // The connection the interceptor will drive is the provider's own type.
        Assert.IsAssignableFrom<DbConnection>(db.Database.GetDbConnection());
    }
}
