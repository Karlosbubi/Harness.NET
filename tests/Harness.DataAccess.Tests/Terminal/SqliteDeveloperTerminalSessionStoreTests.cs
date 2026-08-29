using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Terminal;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Terminal;

public sealed class SqliteDeveloperTerminalSessionStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-terminal-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_only_bounded_lifecycle_metadata()
    {
        StubPaths paths = new(Paths());
        DatabaseInitializationResult initialized =
            await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperTerminalSessionStore store = new(paths);
        DateTimeOffset started = DateTimeOffset.Parse("2026-08-29T20:00:00Z");

        await store.StartAsync(Start("terminal-a", started));
        await store.UpdateDimensionsAsync(new("terminal-a"), new(120, 42));
        await store.CompleteAsync(new(
            new("terminal-a"), StoredTerminalSessionState.Exited,
            started.AddSeconds(3), 0, null, null));

        StoredTerminalSession session = Assert.Single(await store.ListAsync(
            new("workspace-a"), new("goal-a"), 10));
        Assert.Equal(40, initialized.SchemaVersion.Value);
        Assert.Equal(StoredTerminalSessionState.Exited, session.State);
        Assert.Equal(new StoredTerminalDimensions(120, 42), session.Dimensions);
        Assert.Equal("main", session.SourceBranch?.Value);
        Assert.Equal(StoredTerminalContentPolicy.Transient, session.ContentPolicy);
        Assert.Equal(0, session.ExitCode);

        await using SqliteConnection connection = new($"Data Source={paths.Current.DatabasePath}");
        await connection.OpenAsync();
        string[] columns = (await connection.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('developer_terminal_sessions');")).ToArray();
        Assert.DoesNotContain(columns, column =>
            column.Contains("stdout", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("stderr", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("scrollback", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("environment_value", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("executable", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Restart_reconciliation_is_cutoff_bounded_and_one_shot()
    {
        StubPaths paths = new(Paths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperTerminalSessionStore store = new(paths);
        DateTimeOffset cutoff = DateTimeOffset.Parse("2026-08-29T21:00:00Z");
        await store.StartAsync(Start("old", cutoff.AddMinutes(-1)));
        await store.StartAsync(Start("at-cutoff", cutoff));

        int changed = await store.InterruptRunningAsync(cutoff.AddSeconds(1), cutoff);
        await store.StartAsync(Start("later", cutoff.AddMinutes(1)));
        int repeated = await store.InterruptRunningAsync(cutoff.AddMinutes(2),
            cutoff.AddMinutes(2));

        IReadOnlyList<StoredTerminalSession> sessions = await store.ListAsync(
            new("workspace-a"), new("goal-a"), 10);
        Assert.Equal(1, changed);
        Assert.Equal(0, repeated);
        Assert.Equal(StoredTerminalSessionState.Interrupted,
            sessions.Single(item => item.Id.Value == "old").State);
        Assert.Equal(StoredTerminalSessionState.Running,
            sessions.Single(item => item.Id.Value == "at-cutoff").State);
        Assert.Equal(StoredTerminalSessionState.Running,
            sessions.Single(item => item.Id.Value == "later").State);
    }

    [Fact]
    public async Task Retains_twenty_sessions_per_workspace_goal_context()
    {
        StubPaths paths = new(Paths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperTerminalSessionStore store = new(paths);
        DateTimeOffset started = DateTimeOffset.Parse("2026-08-29T22:00:00Z");
        for (int index = 0; index < 21; index++)
        {
            await store.StartAsync(Start($"terminal-{index:D2}", started.AddSeconds(index)));
        }

        IReadOnlyList<StoredTerminalSession> sessions = await store.ListAsync(
            new("workspace-a"), new("goal-a"), 100);
        Assert.Equal(20, sessions.Count);
        Assert.DoesNotContain(sessions, item => item.Id.Value == "terminal-00");
        Assert.Equal("terminal-20", sessions[0].Id.Value);
    }

    private static StoredTerminalSessionStart Start(string id, DateTimeOffset startedAt) => new(
        new(id), new("workspace-a"), new("goal-a"),
        StoredTerminalSourceScope.OriginalWorkspace, new("main"),
        new("Original workspace · user-editable source context"), new("."), new("bash"),
        StoredTerminalEnvironmentProfile.InheritedLocked,
        StoredTerminalContentPolicy.Transient, new(100, 30), startedAt);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
