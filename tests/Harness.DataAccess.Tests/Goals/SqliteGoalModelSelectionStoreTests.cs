using Harness.DataAccess.Configuration;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Goals;

public sealed class SqliteGoalModelSelectionStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-goal-model-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_one_replaceable_selection_per_goal_role()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        InsertGoal(paths.Current.DatabasePath);
        SqliteGoalModelSelectionStore store = new(paths);
        DateTimeOffset firstAt = DateTimeOffset.Parse("2026-07-28T10:00:00Z");
        DateTimeOffset secondAt = firstAt.AddMinutes(1);

        await store.SaveAsync(new("goal-1", "Lead", "Ollama", "local-model", firstAt));
        StoredGoalModelSelection replaced = await store.SaveAsync(new(
            "goal-1",
            "Lead",
            "OpenRouter",
            "remote-model",
            secondAt));
        await store.SaveAsync(new("goal-1", "Reviewer", "Ollama", "review-model", secondAt));
        IReadOnlyList<StoredGoalModelSelection> selections = await store.ListAsync("goal-1");

        Assert.Equal("OpenRouter", replaced.Provider);
        Assert.Equal(secondAt, replaced.SelectedAt);
        Assert.Equal(2, selections.Count);
        Assert.Equal("Lead", selections[0].Role);
        Assert.Equal("remote-model", selections[0].Model);
        Assert.Equal("Reviewer", selections[1].Role);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(root, "config"),
        Path.Combine(root, "data"),
        Path.Combine(root, "state"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    private static void InsertGoal(string databasePath)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspaces (
                id, root_path, name, entry_point, is_trusted, branch, is_dirty,
                created_at, updated_at, is_active)
            VALUES (
                'workspace-1', '/tmp/example', 'Example', '/tmp/example/Example.slnx',
                1, 'main', 0, $now, $now, 1);
            INSERT INTO goals (
                id, workspace_id, title, objective, review_cycle_limit,
                remote_budget_microusd, state, created_at, updated_at)
            VALUES ('goal-1', 'workspace-1', 'Goal', 'Objective', 2,
                    100, 'Draft', $now, $now);
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
