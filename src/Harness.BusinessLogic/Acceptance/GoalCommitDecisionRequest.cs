namespace Harness.BusinessLogic.Acceptance;

public sealed record GoalCommitDecisionRequest(
    GoalCommitApprovalId ApprovalId,
    GoalCommitDecision Decision,
    GoalCommitDecisionReason? Reason);
