using Dapper;
using DbUp;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Persistence;

internal sealed class SqliteDatabaseInitializer(IApplicationPaths applicationPaths)
    : IDatabaseInitializer
{
    public ValueTask<DatabaseInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string databasePath = applicationPaths.Current.DatabasePath;
        string? databaseDirectory = Path.GetDirectoryName(databasePath);
        if (databaseDirectory is null)
        {
            throw new InvalidOperationException("The configured database path has no parent directory.");
        }

        Directory.CreateDirectory(databaseDirectory);
        bool databaseCreated = !File.Exists(databasePath);
        string connectionString = CreateConnectionString(databasePath);

        DbUp.Engine.DatabaseUpgradeResult upgradeResult = DeployChanges.To
            .SqliteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(SqliteDatabaseInitializer).Assembly,
                name => name.Contains(".Persistence.Migrations.", StringComparison.Ordinal))
            .LogToNowhere()
            .Build()
            .PerformUpgrade();

        if (!upgradeResult.Successful)
        {
            throw new InvalidOperationException("SQLite migration failed.", upgradeResult.Error);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using SqliteConnection connection = new(connectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;");
        int schemaVersion = connection.ExecuteScalar<int>(
            "SELECT value FROM application_metadata WHERE key = 'schema_version';");

        return ValueTask.FromResult(new DatabaseInitializationResult(
            databasePath,
            schemaVersion,
            databaseCreated));
    }

    private static string CreateConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString();
}
