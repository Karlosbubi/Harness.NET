namespace Harness.BusinessLogic.Workflows;

public sealed record GoalTaskView(
    GoalTaskId Id,
    GoalTaskSequence Sequence,
    GoalTaskTitle Title,
    GoalTaskObjective Objective,
    GoalTaskFileAreas FileAreas,
    GoalTaskAcceptanceCriteria AcceptanceCriteria,
    GoalTaskState State,
    GoalTaskReport? Report);
