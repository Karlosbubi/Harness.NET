using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Models;

namespace Harness.BusinessLogic.Costs;

internal sealed class RemoteCostService(IRemoteCostStore costStore) : IRemoteCostService
{
    public async ValueTask<RemoteCostReport?> GetAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default)
    {
        if (goalId is null || string.IsNullOrWhiteSpace(goalId.Value))
        {
            return null;
        }

        RemoteCostLedger? ledger = await costStore.GetLedgerAsync(goalId.Value, cancellationToken);
        return ledger is null
            ? null
            : new(
                new(ledger.GoalId),
                new(ledger.CostCap.Value),
                new(ledger.ReservedCost.Value),
                new(ledger.ReconciledCost.Value),
                new(ledger.RemainingCost.Value),
                new(ledger.Overage.Value),
                ledger.Entries.Select(Map).ToArray());
    }

    private static RemoteCostItem Map(RemoteCostEntry entry) => new(
        entry.Id,
        entry.Provider,
        entry.Model,
        entry.Operation switch
        {
            RemoteCostOperation.Chat => RemoteCostKind.Chat,
            RemoteCostOperation.Embedding => RemoteCostKind.Embedding,
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        },
        new(entry.EstimatedCost.Value),
        entry.ActualCost is null ? null : new(entry.ActualCost.Value),
        entry.State switch
        {
            RemoteCostReservationState.Reserved => RemoteCostState.Reserved,
            RemoteCostReservationState.Reconciled => RemoteCostState.Reconciled,
            RemoteCostReservationState.Released => RemoteCostState.Released,
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        },
        entry.CreatedAt,
        entry.CompletedAt);
}
