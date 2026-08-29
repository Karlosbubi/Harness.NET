using System.IO.Compression;
using System.Text.Json;
using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Layouts;
using Harness.DataAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Persistence;

public sealed class SqliteApplicationRestoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-restore-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Inspects_stages_without_live_mutation_then_applies_with_rollback()
    {
        (ApplicationPaths paths, StubApplicationPaths applicationPaths) = Paths("live");
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        await InsertConversationAsync(paths.DatabasePath, "archived");
        Assert.True((await new FileWorkbenchLayoutStore(applicationPaths)
            .WriteAsync(new("{\"Version\":1}"))).Succeeded);
        string archive = Path.Combine(root, "restore.zip");
        await new SqliteApplicationBackup(applicationPaths, new FixedTimeProvider())
            .CreateAsync(new(new(archive)));

        await InsertConversationAsync(paths.DatabasePath, "newer-live-state");
        Assert.True((await new FileWorkbenchLayoutStore(applicationPaths)
            .WriteAsync(new("{\"Version\":2}"))).Succeeded);
        SqliteApplicationRestore restore = new(applicationPaths, new FixedTimeProvider());

        ApplicationRestoreInspectionResult inspection = await restore.InspectAsync(new(archive));
        Assert.NotNull(inspection.Archive);
        Assert.Equal(36, inspection.Archive.SchemaVersion.Value);
        Assert.NotNull(inspection.Archive.WorkbenchLayoutSha256);

        ApplicationRestoreStageResult staged = await restore.StageAsync(
            new(archive), new(await HashAsync(archive)));
        Assert.True(staged.RestartRequired);
        Assert.Equal(1, await CountConversationAsync(paths.DatabasePath, "newer-live-state"));
        Assert.Equal(ApplicationRestoreFailure.PendingRestoreExists,
            (await restore.StageAsync(new(archive), new(await HashAsync(archive)))).Failure);

        // Startup applies before the host opens a pooled database connection.
        SqliteConnection.ClearAllPools();
        ApplicationRestoreApplyResult applied = await restore.ApplyPendingAsync();

        Assert.True(applied.Applied);
        Assert.Equal(1, await CountConversationAsync(paths.DatabasePath, "archived"));
        Assert.Equal(0, await CountConversationAsync(paths.DatabasePath, "newer-live-state"));
        Assert.Equal("{\"Version\":1}",
            (await new FileWorkbenchLayoutStore(applicationPaths).ReadAsync()).Layout?.Value);
        Assert.True(File.Exists(Path.Combine(applied.RollbackDirectory!, "harness.db")));
        Assert.True(File.Exists(Path.Combine(applied.RollbackDirectory!, "workbench-layout.json")));
        Assert.False(Directory.Exists(Path.Combine(paths.DataDirectory, "restores", "pending")));
        Assert.Equal(36,
            (await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync())
            .SchemaVersion.Value);
    }

    [Fact]
    public async Task Rejects_unknown_entries_and_detects_staged_tampering_without_live_mutation()
    {
        (ApplicationPaths paths, StubApplicationPaths applicationPaths) = Paths("tamper");
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        await InsertConversationAsync(paths.DatabasePath, "live");
        string archive = Path.Combine(root, "valid.zip");
        await new SqliteApplicationBackup(applicationPaths, new FixedTimeProvider())
            .CreateAsync(new(new(archive)));
        string unsafeArchive = Path.Combine(root, "unknown.zip");
        File.Copy(archive, unsafeArchive);
        using (ZipArchive zip = ZipFile.Open(unsafeArchive, ZipArchiveMode.Update))
        {
            zip.CreateEntry("../unexpected");
        }

        SqliteApplicationRestore restore = new(applicationPaths, new FixedTimeProvider());
        Assert.Equal(ApplicationRestoreFailure.UnsupportedArchive,
            (await restore.InspectAsync(new(unsafeArchive))).Failure);
        Assert.Null((await restore.StageAsync(
            new(unsafeArchive), new(await HashAsync(unsafeArchive)))).Archive);
        Assert.False(Directory.Exists(Path.Combine(paths.DataDirectory, "restores", "pending")));

        ApplicationRestoreStageResult changed = await restore.StageAsync(
            new(archive), new(new string('0', 64)));
        Assert.Equal(ApplicationRestoreFailure.IntegrityMismatch, changed.Failure);
        Assert.Contains("changed after inspection", changed.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(paths.DataDirectory, "restores", "pending")));

        Assert.NotNull((await restore.StageAsync(
            new(archive), new(await HashAsync(archive)))).Archive);
        await File.AppendAllTextAsync(
            Path.Combine(paths.DataDirectory, "restores", "pending", "harness.db"), "tamper");
        ApplicationRestoreApplyResult applied = await restore.ApplyPendingAsync();

        Assert.False(applied.Applied);
        Assert.Equal(ApplicationRestoreFailure.IntegrityMismatch, applied.Failure);
        Assert.Equal(1, await CountConversationAsync(paths.DatabasePath, "live"));
    }

    [Fact]
    public async Task Applies_version_one_archive_to_fresh_install_and_removes_absent_layout()
    {
        (ApplicationPaths sourcePaths, StubApplicationPaths source) = Paths("v1-source");
        await new SqliteDatabaseInitializer(source).InitializeAsync();
        await InsertConversationAsync(sourcePaths.DatabasePath, "portable");
        string v1 = Path.Combine(root, "v1.zip");
        await CreateVersionOneArchiveAsync(sourcePaths.DatabasePath, v1);

        (ApplicationPaths targetPaths, StubApplicationPaths target) = Paths("v1-target");
        Directory.CreateDirectory(targetPaths.StateDirectory);
        await File.WriteAllTextAsync(targetPaths.WorkbenchLayoutPath, "old layout");
        SqliteApplicationRestore restore = new(target, new FixedTimeProvider());

        Assert.Equal(ApplicationBackupFormat.Version1,
            (await restore.InspectAsync(new(v1))).Archive?.Format);
        Assert.NotNull((await restore.StageAsync(new(v1), new(await HashAsync(v1)))).Archive);
        Assert.True((await restore.ApplyPendingAsync()).Applied);

        Assert.Equal(1, await CountConversationAsync(targetPaths.DatabasePath, "portable"));
        Assert.False(File.Exists(targetPaths.WorkbenchLayoutPath));
        Assert.Equal(36,
            (await new SqliteDatabaseInitializer(target).InitializeAsync()).SchemaVersion.Value);
    }

    [Fact]
    public async Task Publication_failure_restores_previous_database()
    {
        (ApplicationPaths paths, StubApplicationPaths applicationPaths) = Paths("rollback");
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        await InsertConversationAsync(paths.DatabasePath, "archived");
        Assert.True((await new FileWorkbenchLayoutStore(applicationPaths)
            .WriteAsync(new("{\"Version\":1}"))).Succeeded);
        string archive = Path.Combine(root, "rollback.zip");
        await new SqliteApplicationBackup(applicationPaths, new FixedTimeProvider())
            .CreateAsync(new(new(archive)));
        await InsertConversationAsync(paths.DatabasePath, "must-survive");
        SqliteApplicationRestore restore = new(applicationPaths, new FixedTimeProvider());
        Assert.NotNull((await restore.StageAsync(
            new(archive), new(await HashAsync(archive)))).Archive);
        File.Delete(paths.WorkbenchLayoutPath);
        Directory.CreateDirectory(paths.WorkbenchLayoutPath);
        SqliteConnection.ClearAllPools();

        ApplicationRestoreApplyResult result = await restore.ApplyPendingAsync();

        Assert.False(result.Applied);
        Assert.Equal(ApplicationRestoreFailure.ApplyFailed, result.Failure);
        Assert.Equal(1, await CountConversationAsync(paths.DatabasePath, "must-survive"));
    }

    private (ApplicationPaths Paths, StubApplicationPaths ApplicationPaths) Paths(string name)
    {
        string directory = Path.Combine(root, name);
        ApplicationPaths paths = new(
            Path.Combine(directory, "config"), Path.Combine(directory, "data"),
            Path.Combine(directory, "state"), Path.Combine(directory, "cache"),
            Path.Combine(directory, "data", "harness.db"),
            Path.Combine(directory, "state", "logs"),
            Path.Combine(directory, "state", "worktrees"));
        return (paths, new(paths));
    }

    private static async ValueTask InsertConversationAsync(string database, string id)
    {
        await using SqliteConnection connection = new($"Data Source={database}");
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO conversations (id, title, model, created_at, updated_at)
            VALUES (@id, @id, 'model', @now, @now);
            """, new { id, now = DateTimeOffset.UtcNow.ToString("O") });
    }

    private static async ValueTask<int> CountConversationAsync(string database, string id)
    {
        await using SqliteConnection connection = new($"Data Source={database};Mode=ReadOnly");
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM conversations WHERE id = @id;", new { id });
    }

    private static async ValueTask CreateVersionOneArchiveAsync(
        string database,
        string destination)
    {
        string snapshot = destination + ".db";
        await using (SqliteConnection source = new($"Data Source={database};Mode=ReadOnly"))
        await using (SqliteConnection target = new($"Data Source={snapshot}"))
        {
            await source.OpenAsync();
            await target.OpenAsync();
            source.BackupDatabase(target);
        }

        string hash;
        await using (FileStream stream = File.OpenRead(snapshot))
        {
            hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256
                .HashDataAsync(stream));
        }

        using (ZipArchive archive = ZipFile.Open(destination, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(snapshot, "harness.db");
            await using Stream manifest = archive.CreateEntry("manifest.json").Open();
            await JsonSerializer.SerializeAsync(manifest, new
            {
                Format = "harness-backup-v1",
                SchemaVersion = 36,
                CreatedAt = DateTimeOffset.Parse("2026-07-31T12:00:00Z"),
                DatabaseBytes = new FileInfo(snapshot).Length,
                DatabaseSha256 = hash,
                WorkbenchLayout = (object?)null,
            });
        }

        File.Delete(snapshot);
    }

    private static async ValueTask<string> HashAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await System.Security.Cryptography.SHA256
            .HashDataAsync(stream));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-07-31T12:00:00Z");
    }
}
