using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Appouse.EFCore.ReplicaManager.IntegrationTests;

public sealed class MySqlCluster : LiveClusterFixture
{
    private static readonly MySqlServerVersion Version = new(new Version(8, 4, 0));

    protected override string Image => "mysql:8.4";

    protected override string Prefix => "armr-mysql";

    protected override int BasePort => 55632;

    protected override int ContainerPort => 3306;

    protected override string RunArguments => "-e MYSQL_ROOT_PASSWORD=pw -e MYSQL_DATABASE=app";

    protected override TimeSpan ReadyTimeout => TimeSpan.FromSeconds(180);

    protected override string QuotedTable => "`Markers`";

    protected override string QuotedColumn => "`Source`";

    protected override string CreateTableSql =>
        """
        CREATE TABLE IF NOT EXISTS `Markers` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `Source` varchar(200) NOT NULL,
            PRIMARY KEY (`Id`)
        )
        """;

    public override string ConnectionStringFor(int hostPort)
        => $"Server=127.0.0.1;Port={hostPort};Database=app;User ID=root;Password=pw;Connection Timeout=5";

    public override DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    // ServerVersion is passed explicitly rather than via ServerVersion.AutoDetect, which would open a
    // connection while the service provider is being built and make start-up depend on the master.
    public override void Configure(DbContextOptionsBuilder options, string connectionString)
        => options.UseMySql(connectionString, Version);

    public override void ConfigureWithRetry(DbContextOptionsBuilder options, string connectionString)
        => options.UseMySql(connectionString, Version, mySql => mySql.EnableRetryOnFailure(maxRetryCount: 5));
}
