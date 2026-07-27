namespace Harness.BusinessLogic.Costs;

public sealed record RemoteCostItem(
    string Id,
    string Provider,
    string Model,
    RemoteCostKind Kind,
    MicroUsdAmount EstimatedCost,
    MicroUsdAmount? ActualCost,
    RemoteCostState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
