namespace Harness.DataAccess.Models;

public enum RemoteCostReservationFailure
{
    GoalNotApprovedOrAuthorized,
    CostCapExceeded,
}

public sealed record RemoteCostReservationResult(
    RemoteCostReservation? Reservation,
    RemoteCostReservationFailure? Failure);
