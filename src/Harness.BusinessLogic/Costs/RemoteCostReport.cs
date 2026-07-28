using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Costs;

public sealed record RemoteCostReport(
    GoalId GoalId,
    MicroUsdAmount CostCap,
    MicroUsdAmount ReservedCost,
    MicroUsdAmount ReconciledCost,
    MicroUsdAmount RemainingCost,
    MicroUsdAmount Overage,
    IReadOnlyList<RemoteCostItem> Items);
