using Harness.BusinessLogic.Costs;

namespace Harness.BusinessLogic.Goals;

public sealed record GoalCreateRequest(
    string WorkspaceId,
    string Title,
    string Objective,
    ReviewCycleLimit ReviewCycleLimit,
    MicroUsdAmount? RemoteBudget);
