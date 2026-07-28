namespace Harness.DataAccess.Commits;

public sealed record GoalCommitResult(
    GitCommitSha? CommitSha,
    bool WasReconciled,
    string? ErrorCode,
    string? Error);
