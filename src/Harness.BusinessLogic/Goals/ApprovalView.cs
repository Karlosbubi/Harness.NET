namespace Harness.BusinessLogic.Goals;

public sealed record ApprovalView(
    GoalApprovalId Id,
    GoalId GoalId,
    PlanId PlanId,
    ApprovalKind Kind,
    ApprovalDecision Decision,
    string? Reason,
    DateTimeOffset DecidedAt);
