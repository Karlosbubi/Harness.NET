namespace Harness.BusinessLogic.Workflows;

internal sealed record GoalDelegation(
    string? Plan,
    IReadOnlyList<GoalDelegatedTask> Tasks,
    string? Error);

internal sealed record GoalDelegatedTask(
    GoalTaskTitle Title,
    GoalTaskObjective Objective,
    GoalTaskFileAreas FileAreas,
    GoalTaskAcceptanceCriteria AcceptanceCriteria);
