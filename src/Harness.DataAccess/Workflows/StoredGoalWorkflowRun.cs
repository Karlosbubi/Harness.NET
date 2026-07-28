namespace Harness.DataAccess.Workflows;

public sealed record StoredGoalWorkflowRun(
    GoalWorkflowRunId Id,
    GoalWorkflowGoalId GoalId,
    GoalWorkflowRunState State,
    GoalWorkflowReviewCycle ReviewCycle,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
