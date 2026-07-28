using Harness.BusinessLogic.Costs;

namespace Harness.BusinessLogic.Goals;

public sealed record GoalView(
    GoalId Id,
    string WorkspaceId,
    string Title,
    string Objective,
    ReviewCycleLimit ReviewCycleLimit,
    MicroUsdAmount? RemoteBudget,
    GoalState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
