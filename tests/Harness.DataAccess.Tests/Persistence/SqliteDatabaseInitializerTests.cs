using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Persistence;

public sealed class SqliteDatabaseInitializerTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "harness-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Creates_and_migrates_database_idempotently()
    {
        string databasePath = Path.Combine(testDirectory, "data", "harness.db");
        ApplicationPaths paths = new(
            Path.Combine(testDirectory, "config"),
            Path.Combine(testDirectory, "data"),
            Path.Combine(testDirectory, "state"),
            Path.Combine(testDirectory, "cache"),
            databasePath,
            Path.Combine(testDirectory, "state", "logs"),
            Path.Combine(testDirectory, "state", "worktrees"));
        SqliteDatabaseInitializer initializer = new(new StubApplicationPaths(paths));

        DatabaseInitializationResult first = await initializer.InitializeAsync();
        DatabaseInitializationResult second = await initializer.InitializeAsync();

        Assert.True(first.DatabaseCreated);
        Assert.False(second.DatabaseCreated);
        Assert.Equal(4, first.SchemaVersion);
        Assert.Equal(first.SchemaVersion, second.SchemaVersion);
        Assert.True(File.Exists(databasePath));

        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersions';";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
