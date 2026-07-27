namespace Harness.BusinessLogic.Goals;

public sealed record ApprovalView(
    string Id,
    string GoalId,
    string PlanId,
    string Kind,
    string Decision,
    string? Reason,
    DateTimeOffset DecidedAt);
