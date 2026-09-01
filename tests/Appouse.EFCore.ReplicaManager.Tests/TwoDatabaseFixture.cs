using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// Two real SQLite databases with identical schemas but different contents, so a test can tell
/// which one a query actually reached by reading the row back.
/// </summary>
public sealed class TwoDatabaseFixture : IDisposable
{
    private readonly string _root;

    public TwoDatabaseFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "replica-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        MasterConnectionString = Create("master.db", "master");
        ReplicaConnectionString = Create("replica.db", "replica");
        SecondReplicaConnectionString = Create("replica-2.db", "replica-2");
    }

    public string MasterConnectionString { get; }

    public string ReplicaConnectionString { get; }

    public string SecondReplicaConnectionString { get; }

    public DbContextOptions<MarkerContext> OptionsFor(string connectionString)
        => new DbContextOptionsBuilder<MarkerContext>().UseSqlite(connectionString).Options;

    private string Create(string fileName, string source)
    {
        var connectionString = $"Data Source={Path.Combine(_root, fileName)}";

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Markers (Id INTEGER NOT NULL CONSTRAINT PK_Markers PRIMARY KEY AUTOINCREMENT, Source TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();

        command.CommandText = "INSERT INTO Markers (Source) VALUES ($source);";
        command.Parameters.AddWithValue("$source", source);
        command.ExecuteNonQuery();

        return connectionString;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file handle may still be settling on some platforms; the temp directory is
            // disposable either way.
        }
    }
}

public sealed class Marker
{
    public int Id { get; set; }

    public string Source { get; set; } = string.Empty;
}

public sealed class MarkerContext(DbContextOptions<MarkerContext> options) : DbContext(options)
{
    public DbSet<Marker> Markers => Set<Marker>();
}
