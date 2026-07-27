namespace Harness.BusinessLogic.Goals;

public sealed record GoalView(
    string Id,
    string WorkspaceId,
    string Title,
    string Objective,
    int ReviewCycleLimit,
    long? RemoteBudgetMicrousd,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
