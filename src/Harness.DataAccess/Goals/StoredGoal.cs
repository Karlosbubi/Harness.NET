namespace Harness.DataAccess.Goals;

public sealed record StoredGoal(
    string Id,
    string WorkspaceId,
    string Title,
    string Objective,
    int ReviewCycleLimit,
    long? RemoteBudgetMicrousd,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
