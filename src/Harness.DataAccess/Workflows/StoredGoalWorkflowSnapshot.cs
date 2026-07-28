namespace Harness.DataAccess.Workflows;

public sealed record StoredGoalWorkflowSnapshot(
    StoredGoalWorkflowRun Run,
    IReadOnlyList<StoredGoalWorkflowCheckpoint> Checkpoints);
