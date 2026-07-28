namespace Harness.DataAccess.Commits;

using Harness.DataAccess.Workflows;

public interface IGoalCommitApprovalStore
{
    ValueTask<StoredGoalCommitApproval?> GetForRunAsync(
        GoalWorkflowGoalId goalId,
        GoalWorkflowRunId workflowRunId,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalCommitApproval?> GetByIdAsync(
        GoalCommitApprovalId approvalId,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalCommitApprovalStart> CreateAsync(
        StoredGoalCommitApproval approval,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalCommitApproval> DecideAsync(
        GoalCommitApprovalId approvalId,
        GoalCommitApprovalState expectedState,
        GoalCommitApprovalState nextState,
        GoalCommitDecisionReason? decisionReason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalCommitApproval> CompleteAsync(
        GoalCommitApprovalId approvalId,
        GoalCommitApprovalState expectedState,
        GitCommitSha commitSha,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
