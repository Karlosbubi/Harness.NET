using Dapper;
using DbUp;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Persistence;

internal sealed class SqliteDatabaseInitializer : IDatabaseInitializer
{
    internal const int CurrentSchemaVersion = 36;
    private readonly IApplicationPaths applicationPaths;
    private readonly TimeProvider timeProvider;

    public SqliteDatabaseInitializer(
        IApplicationPaths applicationPaths,
        TimeProvider? timeProvider = null)
    {
        this.applicationPaths = applicationPaths;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<DatabaseInitializationResult> InitializeAsync(
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

        DbUp.Engine.UpgradeEngine upgrader = DeployChanges.To
            .SqliteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(SqliteDatabaseInitializer).Assembly,
                name => name.Contains(".Persistence.Migrations.", StringComparison.Ordinal))
            .LogToNowhere()
            .Build();
        BackupArchivePath? preUpgradeBackup = null;
        if (!databaseCreated && upgrader.IsUpgradeRequired())
        {
            string backupDirectory = Path.Combine(
                applicationPaths.Current.DataDirectory, "backups");
            Directory.CreateDirectory(backupDirectory);
            string backupPath = Path.Combine(
                backupDirectory,
                $"pre-upgrade-{timeProvider.GetUtcNow():yyyyMMddTHHmmssfffffffZ}.zip");
            ApplicationBackupResult backup = await new SqliteApplicationBackup(
                    applicationPaths, timeProvider)
                .CreateAsync(new(new(backupPath)), cancellationToken);
            if (backup.Archive is null)
            {
                throw new InvalidOperationException(
                    $"Pre-upgrade backup failed: {backup.Error}");
            }

            preUpgradeBackup = backup.Archive;
        }

        DbUp.Engine.DatabaseUpgradeResult upgradeResult = upgrader.PerformUpgrade();

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

        return new DatabaseInitializationResult(
            new(databasePath),
            new(schemaVersion),
            databaseCreated
                ? DatabaseInitializationKind.Created
                : DatabaseInitializationKind.Existing,
            preUpgradeBackup);
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
