using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Layouts;
using Harness.DataAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Persistence;

public sealed class SqliteApplicationBackupTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-backup-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Creates_nonoverwriting_integrity_checked_portable_archive()
    {
        (ApplicationPaths paths, StubApplicationPaths applicationPaths) = Paths();
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        await using (SqliteConnection connection = new($"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                INSERT INTO conversations (id, title, model, created_at, updated_at)
                VALUES ('backup-proof', 'Private state', 'model', @now, @now);
                """, new { now = DateTimeOffset.UtcNow.ToString("O") });
        }

        string destination = Path.Combine(root, "export.zip");
        Assert.True((await new FileWorkbenchLayoutStore(applicationPaths)
            .WriteAsync(new("{\"Version\":1}"))).Succeeded);
        SqliteApplicationBackup backup = new(applicationPaths, new FixedTimeProvider());

        ApplicationBackupResult result = await backup.CreateAsync(new(new(destination)));

        Assert.Null(result.Error);
        Assert.Equal(30, result.SchemaVersion?.Value);
        Assert.True(File.Exists(destination));
        Assert.Equal(await HashAsync(destination), result.ArchiveSha256?.Value);
        using ZipArchive archive = ZipFile.OpenRead(destination);
        Assert.Equal(["harness.db", "manifest.json", "workbench-layout.json"],
            archive.Entries.Select(entry => entry.FullName).Order().ToArray());
        ZipArchiveEntry manifestEntry = Assert.Single(
            archive.Entries, entry => entry.FullName == "manifest.json");
        using JsonDocument manifest = await JsonDocument.ParseAsync(manifestEntry.Open());
        Assert.Equal("harness-backup-v2",
            manifest.RootElement.GetProperty("Format").GetString());
        Assert.Equal(30, manifest.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(result.DatabaseSha256?.Value,
            manifest.RootElement.GetProperty("DatabaseSha256").GetString());
        JsonElement layoutManifest = manifest.RootElement.GetProperty("WorkbenchLayout");
        Assert.Equal("workbench-layout.json", layoutManifest.GetProperty("Entry").GetString());
        Assert.Equal(result.WorkbenchLayoutSha256?.Value,
            layoutManifest.GetProperty("Sha256").GetString());
        ZipArchiveEntry layoutEntry = archive.GetEntry("workbench-layout.json")!;
        string restoredLayout = Path.Combine(root, "restored-layout.json");
        layoutEntry.ExtractToFile(restoredLayout);
        Assert.Equal(result.WorkbenchLayoutSha256?.Value, await HashAsync(restoredLayout));

        string restored = Path.Combine(root, "restored.db");
        archive.GetEntry("harness.db")!.ExtractToFile(restored);
        Assert.Equal(result.DatabaseSha256?.Value, await HashAsync(restored));
        await using SqliteConnection restoredConnection = new($"Data Source={restored};Mode=ReadOnly");
        await restoredConnection.OpenAsync();
        Assert.Equal("ok", await restoredConnection.ExecuteScalarAsync<string>(
            "PRAGMA integrity_check;"));
        Assert.Equal(1, await restoredConnection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM conversations WHERE id = 'backup-proof';"));

        ApplicationBackupResult duplicate = await backup.CreateAsync(new(new(destination)));
        Assert.Equal(ApplicationBackupFailure.InvalidDestination, duplicate.Failure);
    }

    [Fact]
    public async Task Refuses_to_publish_archive_when_private_layout_is_corrupt()
    {
        (ApplicationPaths paths, StubApplicationPaths applicationPaths) = Paths();
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        Directory.CreateDirectory(paths.StateDirectory);
        await File.WriteAllTextAsync(paths.WorkbenchLayoutPath, "corrupt layout");
        string destination = Path.Combine(root, "corrupt-export.zip");

        ApplicationBackupResult result = await new SqliteApplicationBackup(
                applicationPaths,
                new FixedTimeProvider())
            .CreateAsync(new(new(destination)));

        Assert.Equal(ApplicationBackupFailure.ArchiveCreationFailed, result.Failure);
        Assert.Contains("layout", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Upgrade_creates_verified_pre_migration_recovery_archive()
    {
        (ApplicationPaths paths, StubApplicationPaths applicationPaths) = Paths();
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        await using (SqliteConnection connection = new($"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                INSERT INTO workspaces (
                    id, root_path, name, entry_point, is_trusted, branch, is_dirty,
                    created_at, updated_at, is_active)
                VALUES ('workspace-spend', '/tmp/spend', 'Spend', '/tmp/spend/Spend.slnx',
                    1, 'main', 0, '2026-08-08T00:00:00Z', '2026-08-08T00:00:00Z', 1);
                INSERT INTO goals (
                    id, workspace_id, title, objective, review_cycle_limit,
                    remote_budget_microusd, state, created_at, updated_at)
                VALUES
                    ('draft-spend', 'workspace-spend', 'Draft', 'Draft', 2, NULL,
                     'Draft', '2026-08-08T00:00:00Z', '2026-08-08T00:00:00Z'),
                    ('approved-spend', 'workspace-spend', 'Approved', 'Approved', 2, NULL,
                     'Approved', '2026-08-08T00:00:00Z', '2026-08-08T00:00:00Z');
                DROP TABLE appearance_preferences;
                DROP TABLE agent_role_defaults;
                DROP TABLE goal_budget_extensions;
                DROP TABLE remote_spend_preferences;
                DROP TABLE visual_capture_preferences;
                DROP TABLE editor_intelligence_preferences;
                DROP TABLE keybinding_preferences;
                DROP TABLE keybinding_configuration;
                DROP TABLE developer_dotnet_executions;
                DELETE FROM SchemaVersions
                WHERE ScriptName LIKE '%018_AppearancePreferences.sql'
                   OR ScriptName LIKE '%019_AgentRoleDefaults.sql'
                   OR ScriptName LIKE '%020_RenameEvidence.sql'
                   OR ScriptName LIKE '%021_GoalBudgetExtensions.sql'
                   OR ScriptName LIKE '%022_RemoteSpendPreferences.sql'
                   OR ScriptName LIKE '%023_AgentOutputTokenLimits.sql'
                   OR ScriptName LIKE '%024_RemoveAgentOutputTokenLimits.sql'
                   OR ScriptName LIKE '%025_VisualCapturePreferences.sql'
                   OR ScriptName LIKE '%026_EditorIntelligencePreferences.sql'
                   OR ScriptName LIKE '%027_EditorFormattingPreferences.sql'
                   OR ScriptName LIKE '%028_KeybindingPreferences.sql'
                   OR ScriptName LIKE '%029_EditorInputMode.sql'
                   OR ScriptName LIKE '%030_DeveloperDotNetExecutions.sql';
                UPDATE application_metadata SET value = '17' WHERE key = 'schema_version';
                """);
        }

        DatabaseInitializationResult upgraded = await new SqliteDatabaseInitializer(
            applicationPaths, new FixedTimeProvider()).InitializeAsync();

        Assert.Equal(30, upgraded.SchemaVersion.Value);
        Assert.NotNull(upgraded.PreUpgradeBackup);
        Assert.True(File.Exists(upgraded.PreUpgradeBackup.Value));
        using ZipArchive archive = ZipFile.OpenRead(upgraded.PreUpgradeBackup.Value);
        using JsonDocument manifest = await JsonDocument.ParseAsync(
            archive.GetEntry("manifest.json")!.Open());
        Assert.Equal(17, manifest.RootElement.GetProperty("SchemaVersion").GetInt32());
        await using SqliteConnection current = new($"Data Source={paths.DatabasePath}");
        await current.OpenAsync();
        Assert.Equal(1, await current.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' " +
            "AND name='appearance_preferences';"));
        Assert.Equal(long.MaxValue, await current.ExecuteScalarAsync<long>(
            "SELECT remote_budget_microusd FROM goals WHERE id='draft-spend';"));
        Assert.Null(await current.ExecuteScalarAsync<long?>(
            "SELECT remote_budget_microusd FROM goals WHERE id='approved-spend';"));
        Assert.Equal(1, await current.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' " +
            "AND name='agent_role_defaults';"));
        Assert.Equal(1, await current.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' " +
            "AND name='goal_budget_extensions';"));
    }

    private (ApplicationPaths Paths, StubApplicationPaths ApplicationPaths) Paths()
    {
        ApplicationPaths paths = new(
            Path.Combine(root, "config"), Path.Combine(root, "data"),
            Path.Combine(root, "state"), Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        return (paths, new(paths));
    }

    private static async ValueTask<string> HashAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
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
            DateTimeOffset.Parse("2026-07-29T12:00:00Z");
    }
}
