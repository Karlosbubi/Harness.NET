using Harness.BusinessLogic.Workflows;
using Harness.DataAccess.Workflows;
using WorkflowActorView = Harness.BusinessLogic.Workflows.WorkflowActor;

namespace Harness.BusinessLogic.Tests.Workflows;

public sealed class WalkingSkeletonWorkflowServiceTests
{
    [Fact]
    public async Task Persists_pause_and_resumes_to_reviewed_completion()
    {
        InMemoryWorkflowStore store = new();
        WalkingSkeletonWorkflowService firstProcess = CreateService(store);
        List<WorkflowSnapshot> started = await CollectAsync(firstProcess.StartAsync());

        Assert.Equal([WorkflowState.Running, WorkflowState.Paused],
            started.Select(snapshot => snapshot.State));
        Assert.True(started[^1].CanResume);
        Assert.Equal(WorkflowActorView.Lead, started[^1].Activities[^1].Actor);

        WalkingSkeletonWorkflowService restartedProcess = CreateService(store);
        WorkflowSnapshot persisted = Assert.IsType<WorkflowSnapshot>(
            await restartedProcess.GetLatestAsync());
        List<WorkflowSnapshot> resumed = await CollectAsync(restartedProcess.ResumeAsync());

        Assert.Equal(WorkflowState.Paused, persisted.State);
        Assert.Equal([WorkflowState.Running, WorkflowState.Completed],
            resumed.Select(snapshot => snapshot.State));
        Assert.False(resumed[^1].CanResume);
        Assert.Equal(4, resumed[^1].Activities.Count);
        Assert.Equal(3, resumed[^1].Evidence.Count);
        Assert.Equal(WorkflowActorView.Reviewer, resumed[^1].Activities[^1].Actor);
    }

    [Fact]
    public async Task Resumes_after_interruption_at_an_already_persisted_boundary()
    {
        InMemoryWorkflowStore store = new();
        WalkingSkeletonWorkflowService service = CreateService(store);
        await CollectAsync(service.StartAsync());
        await using (IAsyncEnumerator<WorkflowSnapshot> enumerator =
                     service.ResumeAsync().GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(WorkflowState.Running, enumerator.Current.State);
        }

        WalkingSkeletonWorkflowService restartedProcess = CreateService(store);
        List<WorkflowSnapshot> resumed = await CollectAsync(restartedProcess.ResumeAsync());

        WorkflowSnapshot completed = Assert.Single(resumed);
        Assert.Equal(WorkflowState.Completed, completed.State);
        Assert.Equal(
            [
                WorkflowStage.Started,
                WorkflowStage.Planning,
                WorkflowStage.Implementing,
                WorkflowStage.Reviewing,
            ],
            completed.Activities.Select(activity => activity.Stage));
    }

    [Fact]
    public async Task Does_not_replace_an_incomplete_persisted_run()
    {
        InMemoryWorkflowStore store = new();
        WalkingSkeletonWorkflowService service = CreateService(store);
        await CollectAsync(service.StartAsync());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CollectAsync(service.StartAsync()));

        Assert.Contains("Resume", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, store.Snapshot?.Checkpoints.Count);
    }

    private static WalkingSkeletonWorkflowService CreateService(
        IWorkflowCheckpointStore store) => new(store, new FixedTimeProvider());

    private static async Task<List<WorkflowSnapshot>> CollectAsync(
        IAsyncEnumerable<WorkflowSnapshot> snapshots)
    {
        List<WorkflowSnapshot> result = [];
        await foreach (WorkflowSnapshot snapshot in snapshots)
        {
            result.Add(snapshot);
        }

        return result;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-07-28T14:00:00Z");
    }

    private sealed class InMemoryWorkflowStore : IWorkflowCheckpointStore
    {
        internal StoredWorkflowSnapshot? Snapshot { get; private set; }

        public ValueTask<StoredWorkflowSnapshot?> GetLatestAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask<StoredWorkflowSnapshot> StartAsync(
            StoredWorkflowRun run,
            StoredWorkflowCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Snapshot = new(run, [checkpoint]);
            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask<StoredWorkflowSnapshot> AppendAsync(
            StoredWorkflowCheckpoint checkpoint,
            WorkflowCheckpointKind expectedCheckpoint,
            WorkflowRunState expectedState,
            WorkflowRunState nextState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Snapshot is null ||
                Snapshot.Run.State != expectedState ||
                Snapshot.Checkpoints[^1].Kind != expectedCheckpoint)
            {
                throw new InvalidOperationException("Stale workflow transition.");
            }

            StoredWorkflowCheckpoint appended = checkpoint with
            {
                Sequence = Snapshot.Checkpoints.Count + 1,
            };
            Snapshot = new(
                Snapshot.Run with { State = nextState, UpdatedAt = checkpoint.CreatedAt },
                [.. Snapshot.Checkpoints, appended]);
            return ValueTask.FromResult(Snapshot);
        }
    }
}
