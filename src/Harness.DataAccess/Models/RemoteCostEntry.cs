namespace Harness.DataAccess.Models;

public sealed record RemoteCostEntry(
    string Id,
    string Provider,
    string Model,
    RemoteCostOperation Operation,
    MicroUsd EstimatedCost,
    MicroUsd? ActualCost,
    RemoteCostReservationState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
