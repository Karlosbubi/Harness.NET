namespace Harness.DataAccess.Commits;

using Harness.DataAccess.Workflows;

public sealed record StoredGoalCommitApproval(
    GoalCommitApprovalId Id,
    GoalWorkflowGoalId GoalId,
    GoalWorkflowRunId WorkflowRunId,
    GitBranchName Branch,
    GitCommitSha ExpectedHead,
    GitDiffSha256 DiffSha256,
    GoalCommitDiff Diff,
    GoalCommitChangedFileCount ChangedFileCount,
    GitCommitMessage CommitMessage,
    GitAuthorName AuthorName,
    GitAuthorEmail AuthorEmail,
    GoalCommitApprovalState State,
    GoalCommitDecisionReason? DecisionReason,
    GitCommitSha? CommitSha,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? CompletedAt);
