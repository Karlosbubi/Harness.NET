using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workflows;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Workflows;

public sealed class SqliteGoalWorkflowTaskStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-goal-task-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_ordered_bounded_task_lifecycle()
    {
        (SqliteGoalWorkflowTaskStore store, GoalWorkflowRunId runId) =
            await CreateStoreAsync();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-29T08:00:00Z");
        StoredGoalWorkflowTask first = Task(runId, 1, now);
        StoredGoalWorkflowTask second = Task(runId, 2, now);

        await store.CreateAsync(runId, [first, second]);
        StoredGoalWorkflowTask started = await store.StartAsync(
            first.Id, now.AddMinutes(1));
        StoredGoalWorkflowTask completed = await store.CompleteAsync(
            first.Id, new("Build and focused tests passed."), now.AddMinutes(2));
        IReadOnlyList<StoredGoalWorkflowTask> reloaded = await store.ListAsync(runId);

        Assert.Equal(GoalWorkflowTaskState.InProgress, started.State);
        Assert.Equal(GoalWorkflowTaskState.Completed, completed.State);
        Assert.Equal("Build and focused tests passed.", completed.Report?.Value);
        Assert.Equal([1, 2], reloaded.Select(task => task.Sequence.Value));
        Assert.Equal(
            [GoalWorkflowTaskState.Completed, GoalWorkflowTaskState.Pending],
            reloaded.Select(task => task.State));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.StartAsync(first.Id, now.AddMinutes(3)));
    }

    [Fact]
    public async Task Rejects_noncontiguous_delegation_atomically()
    {
        (SqliteGoalWorkflowTaskStore store, GoalWorkflowRunId runId) =
            await CreateStoreAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.CreateAsync(runId, [Task(runId, 2, now)]));

        Assert.Empty(await store.ListAsync(runId));
    }

    private async ValueTask<(SqliteGoalWorkflowTaskStore Store, GoalWorkflowRunId RunId)>
        CreateStoreAsync()
    {
        ApplicationPaths paths = new(
            Path.Combine(root, "config"), Path.Combine(root, "data"),
            Path.Combine(root, "state"), Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        string workspaceId = Guid.NewGuid().ToString("N");
        string goalId = Guid.NewGuid().ToString("N");
        GoalWorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = new($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO workspaces (
                id, root_path, name, entry_point, is_trusted, is_active,
                branch, is_dirty, created_at, updated_at)
            VALUES (@workspaceId, '/repo', 'repo', '/repo/Harness.slnx', 1, 1,
                    'main', 0, @now, @now);
            INSERT INTO goals (
                id, workspace_id, title, objective, review_cycle_limit,
                remote_budget_microusd, state, created_at, updated_at)
            VALUES (@goalId, @workspaceId, 'Goal', 'Objective', 2, NULL,
                    'Approved', @now, @now);
            INSERT INTO goal_workflow_runs (
                id, goal_id, state, review_cycle, created_at, updated_at)
            VALUES (@runId, @goalId, 'Running', 0, @now, @now);
            """, new { workspaceId, goalId, runId = runId.Value, now });
        return (new(applicationPaths), runId);
    }

    private static StoredGoalWorkflowTask Task(
        GoalWorkflowRunId runId,
        int sequence,
        DateTimeOffset now) => new(
        new(Guid.NewGuid().ToString("N")), runId, new(sequence), new($"Task {sequence}"),
        new("Implement one bounded outcome."), new("src/Feature"),
        new("- Focused tests pass"), GoalWorkflowTaskState.Pending,
        Report: null, now, StartedAt: null, CompletedAt: null);

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
}
