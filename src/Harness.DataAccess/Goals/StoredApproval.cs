namespace Harness.DataAccess.Goals;

public sealed record StoredApproval(
    string Id,
    string GoalId,
    string PlanId,
    string Kind,
    string Decision,
    string? Reason,
    DateTimeOffset DecidedAt);
