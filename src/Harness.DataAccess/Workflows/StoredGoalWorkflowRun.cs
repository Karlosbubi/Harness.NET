namespace Harness.DataAccess.Workflows;

public sealed record StoredGoalWorkflowRun(
    GoalWorkflowRunId Id,
    GoalWorkflowGoalId GoalId,
    GoalWorkflowRunState State,
    int ReviewCycle,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
