namespace Harness.DataAccess.Workflows;

public interface IGoalWorkflowStore
{
    ValueTask<StoredGoalWorkflowSnapshot?> GetLatestAsync(
        GoalWorkflowGoalId goalId,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalWorkflowSnapshot> StartAsync(
        StoredGoalWorkflowRun run,
        StoredGoalWorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalWorkflowSnapshot> AppendAsync(
        StoredGoalWorkflowCheckpoint checkpoint,
        GoalWorkflowCheckpointKind expectedCheckpoint,
        GoalWorkflowRunState expectedState,
        GoalWorkflowRunState nextState,
        CancellationToken cancellationToken = default,
        GoalWorkflowReviewCycle? nextReviewCycle = null);
}
