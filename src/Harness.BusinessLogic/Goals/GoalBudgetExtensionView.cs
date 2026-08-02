using Harness.BusinessLogic.Costs;

namespace Harness.BusinessLogic.Goals;

public sealed record GoalBudgetExtensionView(
    GoalBudgetExtensionId Id,
    GoalId GoalId,
    MicroUsdAmount? PreviousBudget,
    MicroUsdAmount NewBudget,
    GoalBudgetExtensionReason Reason,
    DateTimeOffset ApprovedAt);
