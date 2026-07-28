namespace Harness.DataAccess.Workflows;

public sealed record StoredGoalWorkflowTask(
    GoalWorkflowTaskId Id,
    GoalWorkflowRunId RunId,
    GoalWorkflowTaskSequence Sequence,
    GoalWorkflowTaskTitle Title,
    GoalWorkflowTaskObjective Objective,
    GoalWorkflowTaskFileAreas FileAreas,
    GoalWorkflowTaskAcceptanceCriteria AcceptanceCriteria,
    GoalWorkflowTaskState State,
    GoalWorkflowTaskReport? Report,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
