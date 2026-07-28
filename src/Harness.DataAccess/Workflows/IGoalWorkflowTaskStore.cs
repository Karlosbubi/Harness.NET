namespace Harness.DataAccess.Workflows;

public interface IGoalWorkflowTaskStore
{
    ValueTask<IReadOnlyList<StoredGoalWorkflowTask>> CreateAsync(
        GoalWorkflowRunId runId,
        IReadOnlyList<StoredGoalWorkflowTask> tasks,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredGoalWorkflowTask>> ListAsync(
        GoalWorkflowRunId runId,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalWorkflowTask> StartAsync(
        GoalWorkflowTaskId taskId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalWorkflowTask> CompleteAsync(
        GoalWorkflowTaskId taskId,
        GoalWorkflowTaskReport report,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
