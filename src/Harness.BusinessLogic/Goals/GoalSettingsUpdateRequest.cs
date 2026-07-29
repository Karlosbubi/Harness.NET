using Harness.BusinessLogic.Costs;

namespace Harness.BusinessLogic.Goals;

public sealed record GoalSettingsUpdateRequest(
    GoalId GoalId,
    ReviewCycleLimit ReviewCycleLimit,
    MicroUsdAmount? RemoteBudget,
    DateTimeOffset ExpectedUpdatedAt);
