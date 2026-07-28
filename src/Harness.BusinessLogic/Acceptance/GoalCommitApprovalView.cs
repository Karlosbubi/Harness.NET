using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Acceptance;

public sealed record GoalCommitApprovalView(
    GoalCommitApprovalId Id,
    GoalId GoalId,
    GoalWorkflowId RunId,
    GoalCommitBranch Branch,
    GoalCommitHead ExpectedHead,
    GoalCommitDiffHash DiffHash,
    GoalCommitDiff Diff,
    GoalCommitChangedFileCount ChangedFileCount,
    GoalCommitMessage CommitMessage,
    GoalCommitAuthorName AuthorName,
    GoalCommitAuthorEmail AuthorEmail,
    GoalCommitApprovalState State,
    GoalCommitDecisionReason? DecisionReason,
    GoalCommitHead? CommitSha,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? CompletedAt);
