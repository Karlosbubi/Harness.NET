namespace Harness.BusinessLogic.Acceptance;

public sealed record GoalCommitApprovalResult(
    GoalCommitApprovalView? Approval,
    bool WasReconciled,
    string? ErrorCode,
    string? Error);
