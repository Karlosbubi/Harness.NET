using Harness.DataAccess.Configuration;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Execution;

public sealed class SqliteDeveloperDotNetExecutionStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-developer-run-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_bounded_run_metadata_without_raw_process_output()
    {
        StubPaths paths = new(Paths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperDotNetExecutionStore store = new(paths);
        DateTimeOffset started = DateTimeOffset.Parse("2026-08-13T10:00:00Z");
        await store.StartAsync(new(
            new("run-a"), new("workspace-a"), null, new("Original workspace"),
            StoredDeveloperExecutionOperation.Run,
            new("src/App/App.csproj"), new("net10.0"), null,
            new("M:Program.Main"), started));

        await store.CompleteAsync(new(
            new("run-a"), StoredDeveloperExecutionState.Succeeded,
            started.AddSeconds(2), 0, 2000, null, null));
        StoredDeveloperExecution execution = Assert.Single(await store.ListAsync(
            new("workspace-a"), null, 10));

        Assert.Equal(StoredDeveloperExecutionState.Succeeded, execution.State);
        Assert.Equal(StoredDeveloperExecutionOperation.Run, execution.Operation);
        Assert.Null(execution.Configuration);
        Assert.Equal("M:Program.Main", execution.DeclarationId?.Value);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal(2000, execution.DurationMilliseconds);
        Assert.DoesNotContain(execution.GetType().GetProperties(), property =>
            property.Name.Contains("Output", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("ErrorStream", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Persists_typed_build_operation_and_configuration_without_entry_point()
    {
        StubPaths paths = new(Paths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperDotNetExecutionStore store = new(paths);
        DateTimeOffset started = DateTimeOffset.Parse("2026-08-29T10:00:00Z");

        await store.StartAsync(new(
            new("build-a"), new("workspace-a"), null, new("Original workspace"),
            StoredDeveloperExecutionOperation.Rebuild,
            new("src/App/App.csproj"), null, new("Release"), null, started));

        StoredDeveloperExecution execution = Assert.Single(await store.ListAsync(
            new("workspace-a"), null, 10));
        Assert.Equal(StoredDeveloperExecutionOperation.Rebuild, execution.Operation);
        Assert.Equal("Release", execution.Configuration?.Value);
        Assert.Null(execution.DeclarationId);
    }

    [Theory]
    [InlineData(StoredDeveloperTestScope.Exact, "Demo.CalculatorTests.Adds")]
    [InlineData(StoredDeveloperTestScope.Type, "Demo.CalculatorTests")]
    [InlineData(StoredDeveloperTestScope.Project, "tests/App.Tests.csproj")]
    public async Task Persists_typed_test_identity_without_raw_output(
        StoredDeveloperTestScope scope,
        string selector)
    {
        StubPaths paths = new(Paths());
        DatabaseInitializationResult initialized =
            await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperDotNetExecutionStore store = new(paths);
        DateTimeOffset started = DateTimeOffset.Parse("2026-08-29T11:00:00Z");

        await store.StartAsync(new(
            new("test-a"), new("workspace-a"), null, new("Original workspace"),
            StoredDeveloperExecutionOperation.Test,
            new("tests/App.Tests.csproj"), new("net10.0"), new("Release"), null,
            started,
            new(new string('a', 64)),
            new(selector),
            scope));

        StoredDeveloperExecution execution = Assert.Single(await store.ListAsync(
            new("workspace-a"), null, 10));
        Assert.Equal(34, initialized.SchemaVersion.Value);
        Assert.Equal(StoredDeveloperExecutionOperation.Test, execution.Operation);
        Assert.Equal(new string('a', 64), execution.TestId?.Value);
        Assert.Equal(selector, execution.TestName?.Value);
        Assert.Equal(scope, execution.TestScope);
        Assert.Null(execution.DeclarationId);
    }

    [Fact]
    public async Task Migration_classifies_schema_33_tests_as_exact()
    {
        StubPaths paths = new(Paths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        await using (SqliteConnection connection = new($"Data Source={paths.Current.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE developer_dotnet_executions;
                CREATE TABLE developer_dotnet_executions (
                    id TEXT PRIMARY KEY,
                    workspace_id TEXT NOT NULL,
                    goal_id TEXT NULL,
                    source_description TEXT NOT NULL,
                    project_path TEXT NOT NULL,
                    target_framework TEXT NULL,
                    declaration_id TEXT NOT NULL,
                    state TEXT NOT NULL CHECK (state IN ('Running', 'Succeeded', 'Failed', 'Cancelled', 'Interrupted')),
                    started_at TEXT NOT NULL,
                    completed_at TEXT NULL,
                    exit_code INTEGER NULL,
                    duration_milliseconds INTEGER NOT NULL DEFAULT 0 CHECK (duration_milliseconds >= 0),
                    error_code TEXT NULL,
                    error TEXT NULL,
                    operation TEXT NOT NULL CHECK (operation IN ('Run', 'Build', 'Rebuild', 'Test')),
                    configuration TEXT NULL,
                    test_id TEXT NULL,
                    test_name TEXT NULL,
                    CHECK ((operation = 'Run' AND declaration_id <> '') OR
                        (operation <> 'Run' AND declaration_id = '')),
                    CHECK ((operation = 'Test' AND test_id IS NOT NULL AND test_name IS NOT NULL) OR
                        (operation <> 'Test' AND test_id IS NULL AND test_name IS NULL))
                ) STRICT;
                CREATE INDEX ix_developer_dotnet_executions_context_started
                ON developer_dotnet_executions (workspace_id, goal_id, started_at DESC);
                INSERT INTO developer_dotnet_executions (
                    id, workspace_id, source_description, project_path, declaration_id,
                    state, started_at, operation, test_id, test_name)
                VALUES (
                    'test-before-scope', 'workspace-a', 'Original workspace',
                    'tests/App.Tests.csproj', '', 'Succeeded',
                    '2026-08-29T11:00:00.0000000+00:00', 'Test',
                    'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
                    'Demo.Tests.Passes');
                DELETE FROM SchemaVersions
                WHERE ScriptName LIKE '%034_DeveloperDotNetTestScopes.sql';
                UPDATE application_metadata SET value = '33' WHERE key = 'schema_version';
                """;
            await command.ExecuteNonQueryAsync();
        }

        DatabaseInitializationResult migrated =
            await new SqliteDatabaseInitializer(paths).InitializeAsync();
        StoredDeveloperExecution execution = Assert.Single(
            await new SqliteDeveloperDotNetExecutionStore(paths).ListAsync(
                new("workspace-a"), null, 10));

        Assert.Equal(34, migrated.SchemaVersion.Value);
        Assert.Equal(StoredDeveloperTestScope.Exact, execution.TestScope);
    }

    [Fact]
    public async Task Migration_classifies_existing_developer_rows_as_run()
    {
        StubPaths paths = new(Paths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        await using (SqliteConnection connection = new($"Data Source={paths.Current.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE developer_dotnet_executions;
                CREATE TABLE developer_dotnet_executions (
                    id TEXT PRIMARY KEY,
                    workspace_id TEXT NOT NULL,
                    goal_id TEXT NULL,
                    source_description TEXT NOT NULL,
                    project_path TEXT NOT NULL,
                    target_framework TEXT NULL,
                    declaration_id TEXT NOT NULL,
                    state TEXT NOT NULL CHECK (state IN ('Running', 'Succeeded', 'Failed', 'Cancelled', 'Interrupted')),
                    started_at TEXT NOT NULL,
                    completed_at TEXT NULL,
                    exit_code INTEGER NULL,
                    duration_milliseconds INTEGER NOT NULL DEFAULT 0 CHECK (duration_milliseconds >= 0),
                    error_code TEXT NULL,
                    error TEXT NULL
                ) STRICT;
                CREATE INDEX ix_developer_dotnet_executions_context_started
                ON developer_dotnet_executions (workspace_id, goal_id, started_at DESC);
                INSERT INTO developer_dotnet_executions (
                    id, workspace_id, source_description, project_path, declaration_id,
                    state, started_at)
                VALUES (
                    'legacy-run', 'workspace-a', 'Original workspace', 'App.csproj',
                    'M:Program.Main', 'Succeeded', '2026-08-29T10:00:00.0000000+00:00');
                DELETE FROM SchemaVersions
                WHERE ScriptName LIKE '%032_DeveloperDotNetBuildOperations.sql'
                   OR ScriptName LIKE '%033_DeveloperDotNetTestOperations.sql'
                   OR ScriptName LIKE '%034_DeveloperDotNetTestScopes.sql';
                UPDATE application_metadata SET value = '31' WHERE key = 'schema_version';
                """;
            await command.ExecuteNonQueryAsync();
        }

        DatabaseInitializationResult migrated =
            await new SqliteDatabaseInitializer(paths).InitializeAsync();
        StoredDeveloperExecution execution = Assert.Single(
            await new SqliteDeveloperDotNetExecutionStore(paths).ListAsync(
                new("workspace-a"), null, 10));

        Assert.Equal(34, migrated.SchemaVersion.Value);
        Assert.Equal(StoredDeveloperExecutionOperation.Run, execution.Operation);
        Assert.Null(execution.Configuration);
        Assert.Equal("M:Program.Main", execution.DeclarationId?.Value);
    }

    [Fact]
    public async Task Startup_reconciliation_marks_only_running_executions_interrupted()
    {
        StubPaths paths = new(Paths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperDotNetExecutionStore store = new(paths);
        DateTimeOffset started = DateTimeOffset.Parse("2026-08-13T10:00:00Z");
        foreach (string id in new[] { "running", "complete" })
        {
            await store.StartAsync(new(
                new(id), new("workspace-a"), null, new("Original workspace"),
                StoredDeveloperExecutionOperation.Run,
                new("App.csproj"), null, null, new("M:Program.Main"), started));
        }
        await store.CompleteAsync(new(
            new("complete"), StoredDeveloperExecutionState.Succeeded,
            started.AddSeconds(1), 0, 1000, null, null));

        int changed = await store.InterruptRunningAsync(started.AddSeconds(2));
        IReadOnlyList<StoredDeveloperExecution> executions = await store.ListAsync(
            new("workspace-a"), null, 10);

        Assert.Equal(1, changed);
        Assert.Contains(executions, item => item.Id.Value == "running" &&
            item.State is StoredDeveloperExecutionState.Interrupted);
        Assert.Contains(executions, item => item.Id.Value == "complete" &&
            item.State is StoredDeveloperExecutionState.Succeeded);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ApplicationPaths Paths() => new(
        Path.Combine(root, "config"), Path.Combine(root, "data"),
        Path.Combine(root, "state"), Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    private sealed class StubPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
