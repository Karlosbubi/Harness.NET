namespace Harness.DataAccess.Workflows;

public sealed record StoredWorkflowSnapshot(
    StoredWorkflowRun Run,
    IReadOnlyList<StoredWorkflowCheckpoint> Checkpoints);
