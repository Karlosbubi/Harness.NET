namespace Harness.BusinessLogic.Costs;

public sealed record RemoteCostReport(
    string GoalId,
    MicroUsdAmount CostCap,
    MicroUsdAmount ReservedCost,
    MicroUsdAmount ReconciledCost,
    MicroUsdAmount RemainingCost,
    MicroUsdAmount Overage,
    IReadOnlyList<RemoteCostItem> Items);
