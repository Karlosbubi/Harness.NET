using Harness.BusinessLogic.Costs;

namespace Harness.BusinessLogic.Goals;

public sealed record GoalBudgetExtensionRequest(
    GoalId GoalId,
    MicroUsdAmount? ExpectedBudget,
    MicroUsdAmount NewBudget,
    GoalBudgetExtensionReason Reason);
