namespace Harness.DataAccess.Models;

public sealed record RemoteCostReservationRequest(
    string GoalId,
    string Provider,
    string Model,
    RemoteCostOperation Operation,
    MicroUsd EstimatedCost,
    RemoteModelRole? Role = null);
