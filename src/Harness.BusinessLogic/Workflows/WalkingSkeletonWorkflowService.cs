using System.Runtime.CompilerServices;
using Harness.DataAccess.Workflows;
using StoredActor = Harness.DataAccess.Workflows.WorkflowActor;
using StoredCheckpointKind = Harness.DataAccess.Workflows.WorkflowCheckpointKind;
using StoredRunState = Harness.DataAccess.Workflows.WorkflowRunState;

namespace Harness.BusinessLogic.Workflows;

internal sealed class WalkingSkeletonWorkflowService(
    IWorkflowCheckpointStore store,
    TimeProvider timeProvider) : IWalkingSkeletonWorkflowService
{
    public async ValueTask<WorkflowSnapshot?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        StoredWorkflowSnapshot? snapshot = await store.GetLatestAsync(cancellationToken);
        return snapshot is null ? null : ToView(snapshot);
    }

    public async IAsyncEnumerable<WorkflowSnapshot> StartAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StoredWorkflowSnapshot? latest = await store.GetLatestAsync(cancellationToken);
        if (latest is not null && latest.Run.State is not StoredRunState.Completed)
        {
            throw new InvalidOperationException(
                "Resume the persisted workflow before starting another one.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Harness.DataAccess.Workflows.WorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        StoredWorkflowSnapshot started = await store.StartAsync(
            new(runId, StoredRunState.Running, now, now),
            Checkpoint(
                runId,
                StoredCheckpointKind.Started,
                StoredActor.System,
                "Walking-skeleton workflow started.",
                evidenceTitle: null,
                evidenceContent: null,
                now),
            cancellationToken);
        yield return ToView(started);

        cancellationToken.ThrowIfCancellationRequested();
        StoredWorkflowSnapshot paused = await AppendPlanAsync(started, cancellationToken);
        yield return ToView(paused);
    }

    public async IAsyncEnumerable<WorkflowSnapshot> ResumeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StoredWorkflowSnapshot snapshot = await store.GetLatestAsync(cancellationToken) ??
            throw new InvalidOperationException("No persisted workflow is available to resume.");
        StoredWorkflowCheckpoint latest = snapshot.Checkpoints[^1];

        if (latest.Kind is StoredCheckpointKind.Started)
        {
            StoredWorkflowSnapshot paused = await AppendPlanAsync(snapshot, cancellationToken);
            yield return ToView(paused);
            yield break;
        }

        if (latest.Kind is StoredCheckpointKind.PlanProposed)
        {
            snapshot = await store.AppendAsync(
                Checkpoint(
                    snapshot.Run.Id,
                    StoredCheckpointKind.ImplementationProduced,
                    StoredActor.Implementer,
                    "Implementer completed the bounded fake task.",
                    "Implementation evidence",
                    "The deterministic fake adapter produced a scoped change and verification result.",
                    timeProvider.GetUtcNow()),
                StoredCheckpointKind.PlanProposed,
                StoredRunState.Paused,
                StoredRunState.Running,
                cancellationToken);
            yield return ToView(snapshot);
            latest = snapshot.Checkpoints[^1];
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (latest.Kind is StoredCheckpointKind.ImplementationProduced)
        {
            snapshot = await store.AppendAsync(
                Checkpoint(
                    snapshot.Run.Id,
                    StoredCheckpointKind.ReviewCompleted,
                    StoredActor.Reviewer,
                    "Reviewer accepted the bounded fake result.",
                    "Review evidence",
                    "The independent fake review found the checkpoint sequence complete and reproducible.",
                    timeProvider.GetUtcNow()),
                StoredCheckpointKind.ImplementationProduced,
                StoredRunState.Running,
                StoredRunState.Completed,
                cancellationToken);
            yield return ToView(snapshot);
            yield break;
        }

        if (latest.Kind is StoredCheckpointKind.ReviewCompleted)
        {
            yield return ToView(snapshot);
            yield break;
        }

        throw new InvalidOperationException("The persisted workflow checkpoint is not resumable.");
    }

    private async ValueTask<StoredWorkflowSnapshot> AppendPlanAsync(
        StoredWorkflowSnapshot snapshot,
        CancellationToken cancellationToken) =>
        await store.AppendAsync(
            Checkpoint(
                snapshot.Run.Id,
                StoredCheckpointKind.PlanProposed,
                StoredActor.Lead,
                "Lead proposed a bounded fake implementation and review plan.",
                "Proposed plan",
                "1. Produce a deterministic fake change. 2. Verify it. 3. Review the evidence.",
                timeProvider.GetUtcNow()),
            StoredCheckpointKind.Started,
            StoredRunState.Running,
            StoredRunState.Paused,
            cancellationToken);

    private static StoredWorkflowCheckpoint Checkpoint(
        Harness.DataAccess.Workflows.WorkflowRunId runId,
        StoredCheckpointKind kind,
        StoredActor actor,
        string summary,
        string? evidenceTitle,
        string? evidenceContent,
        DateTimeOffset createdAt) => new(
        Guid.NewGuid().ToString("N"),
        runId,
        Sequence: 1,
        kind,
        actor,
        new(summary),
        evidenceTitle is null ? null : new(evidenceTitle),
        evidenceContent is null ? null : new(evidenceContent),
        createdAt);

    private static WorkflowSnapshot ToView(StoredWorkflowSnapshot snapshot) => new(
        new(snapshot.Run.Id.Value),
        snapshot.Run.State switch
        {
            StoredRunState.Running => WorkflowState.Running,
            StoredRunState.Paused => WorkflowState.Paused,
            StoredRunState.Completed => WorkflowState.Completed,
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
        },
        snapshot.Checkpoints.Select(checkpoint => new WorkflowActivityView(
            checkpoint.Sequence,
            checkpoint.Kind switch
            {
                StoredCheckpointKind.Started => WorkflowStage.Started,
                StoredCheckpointKind.PlanProposed => WorkflowStage.Planning,
                StoredCheckpointKind.ImplementationProduced => WorkflowStage.Implementing,
                StoredCheckpointKind.ReviewCompleted => WorkflowStage.Reviewing,
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
            },
            checkpoint.Actor switch
            {
                StoredActor.System => WorkflowActor.System,
                StoredActor.Lead => WorkflowActor.Lead,
                StoredActor.Implementer => WorkflowActor.Implementer,
                StoredActor.Reviewer => WorkflowActor.Reviewer,
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
            },
            new(checkpoint.Summary.Value))).ToArray(),
        snapshot.Checkpoints
            .Where(checkpoint => checkpoint.EvidenceTitle is not null)
            .Select(checkpoint => new WorkflowEvidenceView(
                checkpoint.Sequence,
                new(checkpoint.EvidenceTitle!.Value),
                new(checkpoint.EvidenceContent!.Value)))
            .ToArray(),
        snapshot.Run.State is not StoredRunState.Completed);
}
