namespace Harness.DataAccess.Models;

public sealed record RemoteCostLedger(
    string GoalId,
    MicroUsd CostCap,
    MicroUsd ReservedCost,
    MicroUsd ReconciledCost,
    MicroUsd RemainingCost,
    MicroUsd Overage,
    IReadOnlyList<RemoteCostEntry> Entries);
