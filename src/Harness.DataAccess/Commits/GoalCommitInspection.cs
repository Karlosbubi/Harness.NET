namespace Harness.DataAccess.Commits;

public sealed record GoalCommitInspection(
    GitBranchName? Branch,
    GitCommitSha? Head,
    GitDiffSha256? DiffSha256,
    GoalCommitDiff Diff,
    GoalCommitChangedFileCount ChangedFileCount,
    string? ErrorCode,
    string? Error);
