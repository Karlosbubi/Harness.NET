namespace Harness.DataAccess.Models;

public interface IRemoteCostStore
{
    ValueTask<RemoteCostLedger?> GetLedgerAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    ValueTask<RemoteCostReservationResult> ReserveAsync(
        RemoteCostReservationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask ReconcileAsync(
        string reservationId,
        MicroUsd actualCost,
        CancellationToken cancellationToken = default);

    ValueTask ReleaseAsync(
        string reservationId,
        CancellationToken cancellationToken = default);
}
