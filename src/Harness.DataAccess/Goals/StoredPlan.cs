namespace Harness.DataAccess.Goals;

public sealed record StoredPlan(
    string Id,
    string GoalId,
    int Revision,
    string Content,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
