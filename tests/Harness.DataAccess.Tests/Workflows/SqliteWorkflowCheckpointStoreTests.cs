using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workflows;

namespace Harness.DataAccess.Tests.Workflows;

public sealed class SqliteWorkflowCheckpointStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-workflow-store-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_ordered_checkpoints_and_latest_run_state()
    {
        SqliteWorkflowCheckpointStore store = await CreateStoreAsync();
        WorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset startedAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        StoredWorkflowSnapshot started = await store.StartAsync(
            new(runId, WorkflowRunState.Running, startedAt, startedAt),
            Checkpoint(runId, 1, WorkflowCheckpointKind.Started, startedAt));

        StoredWorkflowSnapshot paused = await store.AppendAsync(
            Checkpoint(runId, 0, WorkflowCheckpointKind.PlanProposed, startedAt.AddMinutes(1)),
            WorkflowCheckpointKind.Started,
            WorkflowRunState.Running,
            WorkflowRunState.Paused);
        StoredWorkflowSnapshot resumed = await store.AppendAsync(
            Checkpoint(
                runId,
                0,
                WorkflowCheckpointKind.ImplementationProduced,
                startedAt.AddMinutes(2)),
            WorkflowCheckpointKind.PlanProposed,
            WorkflowRunState.Paused,
            WorkflowRunState.Running);

        StoredWorkflowSnapshot? latest = await store.GetLatestAsync();

        Assert.Equal(WorkflowRunState.Running, resumed.Run.State);
        Assert.Equal(WorkflowRunState.Paused, paused.Run.State);
        Assert.Equal(WorkflowCheckpointKind.Started, Assert.Single(started.Checkpoints).Kind);
        Assert.NotNull(latest);
        Assert.Equal(runId, latest.Run.Id);
        Assert.Equal([1, 2, 3], latest.Checkpoints.Select(item => item.Sequence));
        Assert.Equal(
            WorkflowCheckpointKind.ImplementationProduced,
            latest.Checkpoints[^1].Kind);
    }

    [Fact]
    public async Task Rejects_a_stale_checkpoint_transition_without_appending()
    {
        SqliteWorkflowCheckpointStore store = await CreateStoreAsync();
        WorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await store.StartAsync(
            new(runId, WorkflowRunState.Running, now, now),
            Checkpoint(runId, 1, WorkflowCheckpointKind.Started, now));
        await store.AppendAsync(
            Checkpoint(runId, 0, WorkflowCheckpointKind.PlanProposed, now.AddSeconds(1)),
            WorkflowCheckpointKind.Started,
            WorkflowRunState.Running,
            WorkflowRunState.Paused);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.AppendAsync(
                Checkpoint(runId, 0, WorkflowCheckpointKind.PlanProposed, now.AddSeconds(2)),
                WorkflowCheckpointKind.Started,
                WorkflowRunState.Running,
                WorkflowRunState.Paused));

        StoredWorkflowSnapshot latest = Assert.IsType<StoredWorkflowSnapshot>(
            await store.GetLatestAsync());
        Assert.Equal(WorkflowRunState.Paused, latest.Run.State);
        Assert.Equal(2, latest.Checkpoints.Count);
    }

    private async ValueTask<SqliteWorkflowCheckpointStore> CreateStoreAsync()
    {
        ApplicationPaths paths = new(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "state"),
            Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"),
            Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        return new(applicationPaths);
    }

    private static StoredWorkflowCheckpoint Checkpoint(
        WorkflowRunId runId,
        int sequence,
        WorkflowCheckpointKind kind,
        DateTimeOffset createdAt) => new(
        Guid.NewGuid().ToString("N"),
        runId,
        sequence,
        kind,
        kind switch
        {
            WorkflowCheckpointKind.Started => WorkflowActor.System,
            WorkflowCheckpointKind.PlanProposed => WorkflowActor.Lead,
            WorkflowCheckpointKind.ImplementationProduced => WorkflowActor.Implementer,
            WorkflowCheckpointKind.ReviewCompleted => WorkflowActor.Reviewer,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        },
        new(kind.ToString()),
        new("Evidence"),
        new("Checkpoint evidence"),
        createdAt);

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
