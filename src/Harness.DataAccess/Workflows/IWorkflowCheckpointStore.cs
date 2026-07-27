namespace Harness.DataAccess.Workflows;

public interface IWorkflowCheckpointStore
{
    ValueTask<StoredWorkflowSnapshot?> GetLatestAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredWorkflowSnapshot> StartAsync(
        StoredWorkflowRun run,
        StoredWorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    ValueTask<StoredWorkflowSnapshot> AppendAsync(
        StoredWorkflowCheckpoint checkpoint,
        WorkflowCheckpointKind expectedCheckpoint,
        WorkflowRunState expectedState,
        WorkflowRunState nextState,
        CancellationToken cancellationToken = default);
}
