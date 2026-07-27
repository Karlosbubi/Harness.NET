using Harness.BusinessLogic.Costs;
using Harness.DataAccess.Models;

namespace Harness.BusinessLogic.Tests.Costs;

public sealed class RemoteCostServiceTests
{
    [Fact]
    public async Task Maps_cost_control_and_attribution_without_data_access_types()
    {
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        RemoteCostLedger ledger = new(
            "goal-1",
            new(100),
            new(20),
            new(60),
            new(20),
            new(0),
            [new(
                "reservation-1",
                "OpenRouter",
                "model",
                RemoteCostOperation.Chat,
                new(80),
                new(60),
                RemoteCostReservationState.Reconciled,
                createdAt,
                createdAt.AddSeconds(1))]);
        RemoteCostService service = new(new StubCostStore(ledger));

        RemoteCostReport report = Assert.IsType<RemoteCostReport>(await service.GetAsync("goal-1"));

        Assert.Equal(new MicroUsdAmount(100), report.CostCap);
        Assert.Equal(new MicroUsdAmount(20), report.ReservedCost);
        Assert.Equal(new MicroUsdAmount(60), report.ReconciledCost);
        RemoteCostItem item = Assert.Single(report.Items);
        Assert.Equal(RemoteCostKind.Chat, item.Kind);
        Assert.Equal(RemoteCostState.Reconciled, item.State);
        Assert.Equal("OpenRouter", item.Provider);
        Assert.Equal("model", item.Model);
    }

    private sealed class StubCostStore(RemoteCostLedger ledger) : IRemoteCostStore
    {
        public ValueTask<RemoteCostLedger?> GetLedgerAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RemoteCostLedger?>(ledger);

        public ValueTask<RemoteCostReservationResult> ReserveAsync(
            RemoteCostReservationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ReconcileAsync(
            string reservationId,
            MicroUsd actualCost,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ReleaseAsync(
            string reservationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
