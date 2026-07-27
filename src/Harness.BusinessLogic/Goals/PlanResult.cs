namespace Harness.BusinessLogic.Goals;

public sealed record PlanResult(
    GoalView? Goal,
    PlanView? Plan,
    ApprovalView? Approval,
    string? ErrorCode,
    string? Error);
