namespace Harness.BusinessLogic.Goals;

public sealed record GoalCreateRequest(
    string WorkspaceId,
    string Title,
    string Objective,
    int ReviewCycleLimit,
    long? RemoteBudgetMicrousd);
