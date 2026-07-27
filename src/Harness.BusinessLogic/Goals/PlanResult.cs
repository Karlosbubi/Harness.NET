namespace Harness.BusinessLogic.Goals;

public sealed record PlanResult(
    GoalView? Goal,
    PlanView? Plan,
    ApprovalView? Approval,
    GoalWorktreeView? Worktree,
    string? ErrorCode,
    string? Error);
