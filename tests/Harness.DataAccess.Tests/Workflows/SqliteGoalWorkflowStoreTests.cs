using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workflows;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Workflows;

public sealed class SqliteGoalWorkflowStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-goal-workflow-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_goal_bound_transitions_and_rejects_stale_appends()
    {
        (SqliteGoalWorkflowStore store, string goalId) = await CreateStoreAsync();
        GoalWorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T18:00:00Z");
        await store.StartAsync(
            new(runId, new(goalId), GoalWorkflowRunState.Running, new(0), now, now),
            Checkpoint(runId, 1, GoalWorkflowCheckpointKind.Started, now));
        await store.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.LeadCallStarted,
                now.AddSeconds(1)),
            GoalWorkflowCheckpointKind.Started,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running);
        StoredGoalWorkflowSnapshot paused = await store.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.PlanProposed,
                now.AddSeconds(2)),
            GoalWorkflowCheckpointKind.LeadCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.AwaitingPlanApproval);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.AppendAsync(
                Checkpoint(runId, 0, GoalWorkflowCheckpointKind.PlanProposed,
                    now.AddSeconds(3)),
                GoalWorkflowCheckpointKind.LeadCallStarted,
                GoalWorkflowRunState.Running,
                GoalWorkflowRunState.AwaitingPlanApproval));

        StoredGoalWorkflowSnapshot latest = Assert.IsType<StoredGoalWorkflowSnapshot>(
            await store.GetLatestAsync(new(goalId)));
        Assert.Equal(GoalWorkflowRunState.AwaitingPlanApproval, paused.Run.State);
        Assert.Equal([1, 2, 3], latest.Checkpoints.Select(item => item.Sequence));
        Assert.Equal(GoalWorkflowCheckpointKind.PlanProposed, latest.Checkpoints[^1].Kind);
    }

    [Fact]
    public async Task Allows_only_one_noncompleted_run_per_goal()
    {
        (SqliteGoalWorkflowStore store, string goalId) = await CreateStoreAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GoalWorkflowRunId first = new(Guid.NewGuid().ToString("N"));
        await store.StartAsync(
            new(first, new(goalId), GoalWorkflowRunState.Running, new(0), now, now),
            Checkpoint(first, 1, GoalWorkflowCheckpointKind.Started, now));
        GoalWorkflowRunId second = new(Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<SqliteException>(async () =>
            await store.StartAsync(
                new(second, new(goalId), GoalWorkflowRunState.Running, new(0), now, now),
                Checkpoint(second, 1, GoalWorkflowCheckpointKind.Started, now)));
    }

    [Theory]
    [InlineData(GoalWorkflowCheckpointKind.LeadCallStarted)]
    [InlineData(GoalWorkflowCheckpointKind.ImplementerCallStarted)]
    [InlineData(GoalWorkflowCheckpointKind.ReviewerCallStarted)]
    public async Task Explicit_retry_reopens_only_a_durable_user_direction_boundary(
        GoalWorkflowCheckpointKind retryKind)
    {
        (SqliteGoalWorkflowStore store, string goalId) = await CreateStoreAsync();
        GoalWorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        await store.StartAsync(
            new(runId, new(goalId), GoalWorkflowRunState.Running, new(0), now, now),
            Checkpoint(runId, 1, GoalWorkflowCheckpointKind.Started, now));
        await store.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.LeadCallStarted, now.AddSeconds(1)),
            GoalWorkflowCheckpointKind.Started,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running);
        StoredGoalWorkflowSnapshot failed = await store.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.UserDirectionRequired,
                now.AddSeconds(2)),
            GoalWorkflowCheckpointKind.LeadCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.NeedsDirection);

        StoredGoalWorkflowSnapshot retried = await store.AppendAsync(
            Checkpoint(runId, 0, retryKind, now.AddSeconds(3)),
            GoalWorkflowCheckpointKind.UserDirectionRequired,
            GoalWorkflowRunState.NeedsDirection,
            GoalWorkflowRunState.Running);

        Assert.Equal(GoalWorkflowRunState.NeedsDirection, failed.Run.State);
        Assert.Equal(GoalWorkflowRunState.Running, retried.Run.State);
        Assert.Equal(retryKind, retried.Checkpoints[^1].Kind);
    }

    [Fact]
    public async Task Review_completion_atomically_advances_the_semantic_cycle()
    {
        (SqliteGoalWorkflowStore store, string goalId) = await CreateStoreAsync();
        GoalWorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T18:00:00Z");
        await store.StartAsync(
            new(runId, new(goalId), GoalWorkflowRunState.Running, new(0), now, now),
            Checkpoint(runId, 1, GoalWorkflowCheckpointKind.Started, now));
        await store.AppendAsync(Checkpoint(runId, 0,
                GoalWorkflowCheckpointKind.LeadCallStarted, now.AddSeconds(1)),
            GoalWorkflowCheckpointKind.Started,
            GoalWorkflowRunState.Running, GoalWorkflowRunState.Running);
        await store.AppendAsync(Checkpoint(runId, 0,
                GoalWorkflowCheckpointKind.PlanProposed, now.AddSeconds(2)),
            GoalWorkflowCheckpointKind.LeadCallStarted,
            GoalWorkflowRunState.Running, GoalWorkflowRunState.AwaitingPlanApproval);
        await store.AppendAsync(Checkpoint(runId, 0,
                GoalWorkflowCheckpointKind.PlanApproved, now.AddSeconds(3)),
            GoalWorkflowCheckpointKind.PlanProposed,
            GoalWorkflowRunState.AwaitingPlanApproval, GoalWorkflowRunState.Running);
        await store.AppendAsync(Checkpoint(runId, 0,
                GoalWorkflowCheckpointKind.ImplementerCallStarted, now.AddSeconds(4)),
            GoalWorkflowCheckpointKind.PlanApproved,
            GoalWorkflowRunState.Running, GoalWorkflowRunState.Running);
        await store.AppendAsync(Checkpoint(runId, 0,
                GoalWorkflowCheckpointKind.ImplementationProduced, now.AddSeconds(5)),
            GoalWorkflowCheckpointKind.ImplementerCallStarted,
            GoalWorkflowRunState.Running, GoalWorkflowRunState.Running);
        await store.AppendAsync(Checkpoint(runId, 0,
                GoalWorkflowCheckpointKind.ImplementerCallStarted, now.AddSeconds(6)),
            GoalWorkflowCheckpointKind.ImplementationProduced,
            GoalWorkflowRunState.Running, GoalWorkflowRunState.Running);
        await store.AppendAsync(Checkpoint(runId, 0,
                GoalWorkflowCheckpointKind.ImplementationProduced, now.AddSeconds(7)),
            GoalWorkflowCheckpointKind.ImplementerCallStarted,
            GoalWorkflowRunState.Running, GoalWorkflowRunState.Running);
        await store.AppendAsync(Checkpoint(runId, 0,
                GoalWorkflowCheckpointKind.ReviewerCallStarted, now.AddSeconds(8)),
            GoalWorkflowCheckpointKind.ImplementationProduced,
            GoalWorkflowRunState.Running, GoalWorkflowRunState.Running);

        StoredGoalWorkflowSnapshot reviewed = await store.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.ReviewCompleted,
                now.AddSeconds(9)),
            GoalWorkflowCheckpointKind.ReviewerCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running,
            nextReviewCycle: new(1));

        Assert.Equal(1, reviewed.Run.ReviewCycle.Value);
        StoredGoalWorkflowSnapshot reloaded = Assert.IsType<StoredGoalWorkflowSnapshot>(
            await store.GetLatestAsync(new(goalId)));
        Assert.Equal(1, reloaded.Run.ReviewCycle.Value);
    }

    [Fact]
    public async Task Abort_is_durable_and_idempotent_without_an_existing_run()
    {
        (SqliteGoalWorkflowStore store, string goalId) = await CreateStoreAsync();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
        WorkflowCheckpointSummary reason = new("Stopped to pursue a different goal.");

        StoredGoalWorkflowSnapshot first = await store.AbortAsync(
            new(goalId), reason, now);
        StoredGoalWorkflowSnapshot second = await store.AbortAsync(
            new(goalId), reason, now.AddMinutes(1));
        StoredGoalWorkflowSnapshot reloaded = Assert.IsType<StoredGoalWorkflowSnapshot>(
            await store.GetLatestAsync(new(goalId)));

        Assert.Equal(GoalWorkflowRunState.Completed, first.Run.State);
        Assert.Equal(first.Run.Id, second.Run.Id);
        Assert.Equal(first.Run.Id, reloaded.Run.Id);
        StoredGoalWorkflowCheckpoint checkpoint = Assert.Single(reloaded.Checkpoints);
        Assert.Equal(GoalWorkflowCheckpointKind.UserDirectionRequired, checkpoint.Kind);
        Assert.Equal(reason.Value, checkpoint.EvidenceContent?.Value);
    }

    [Fact]
    public async Task Abort_terminally_closes_a_paused_needs_direction_run()
    {
        (SqliteGoalWorkflowStore store, string goalId) = await CreateStoreAsync();
        GoalWorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
        await store.StartAsync(
            new(runId, new(goalId), GoalWorkflowRunState.Running, new(0), now, now),
            Checkpoint(runId, 1, GoalWorkflowCheckpointKind.Started, now));
        await store.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.LeadCallStarted, now.AddSeconds(1)),
            GoalWorkflowCheckpointKind.Started,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running);
        await store.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.UserDirectionRequired,
                now.AddSeconds(2)),
            GoalWorkflowCheckpointKind.LeadCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.NeedsDirection);

        StoredGoalWorkflowSnapshot aborted = await store.AbortAsync(
            new(goalId), new("Start over with a narrower objective."), now.AddSeconds(3));

        Assert.Equal(runId, aborted.Run.Id);
        Assert.Equal(GoalWorkflowRunState.Completed, aborted.Run.State);
        Assert.Equal(GoalWorkflowCheckpointKind.UserDirectionRequired,
            aborted.Checkpoints[^1].Kind);
        Assert.Equal("Start over with a narrower objective.",
            aborted.Checkpoints[^1].EvidenceContent?.Value);
    }

    private async ValueTask<(SqliteGoalWorkflowStore Store, string GoalId)> CreateStoreAsync()
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
                    'Draft', @now, @now);
            """, new { workspaceId, goalId, now = DateTimeOffset.UtcNow.ToString("O") });
        return (new(applicationPaths), goalId);
    }

    private static StoredGoalWorkflowCheckpoint Checkpoint(
        GoalWorkflowRunId runId,
        int sequence,
        GoalWorkflowCheckpointKind kind,
        DateTimeOffset createdAt) => new(
        Guid.NewGuid().ToString("N"), runId, sequence, kind, WorkflowActor.System,
        new(kind.ToString()), new("Evidence"), new("Content"), createdAt);

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
