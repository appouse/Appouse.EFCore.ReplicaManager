using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Appouse.EFCore.ReplicaManager.IntegrationTests;

public sealed class SqlServerCluster : LiveClusterFixture
{
    private const string Password = "Str0ng!Passw0rd";

    protected override string Image => "mcr.microsoft.com/mssql/server:2022-latest";

    // The image is published for amd64 only, so on Apple Silicon it runs under emulation.
    protected override string PullArguments => $"--platform linux/amd64 {Image}";

    protected override string Prefix => "armr-mssql";

    protected override int BasePort => 55832;

    protected override int ContainerPort => 1433;

    protected override string RunArguments =>
        $"--platform linux/amd64 -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD={Password} -e MSSQL_PID=Developer";

    protected override TimeSpan ReadyTimeout => TimeSpan.FromSeconds(240);

    protected override string QuotedTable => "[Markers]";

    protected override string QuotedColumn => "[Source]";

    protected override string CreateTableSql =>
        """
        IF OBJECT_ID('dbo.Markers', 'U') IS NULL
            CREATE TABLE [dbo].[Markers] (
                [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Source] nvarchar(200) NOT NULL
            )
        """;

    public override string ConnectionStringFor(int hostPort)
        => $"Server=127.0.0.1,{hostPort};Database=master;User Id=sa;Password={Password};" +
           "Encrypt=False;TrustServerCertificate=True;Connect Timeout=5";

    public override DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public override void Configure(DbContextOptionsBuilder options, string connectionString)
        => options.UseSqlServer(connectionString);

    public override void ConfigureWithRetry(DbContextOptionsBuilder options, string connectionString)
        => options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5));
}
