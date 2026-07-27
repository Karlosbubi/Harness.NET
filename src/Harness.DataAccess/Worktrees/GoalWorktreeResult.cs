namespace Harness.DataAccess.Worktrees;

public sealed record GoalWorktreeResult(
    string GoalId,
    string Branch,
    string Path,
    string BaseCommit,
    bool WasCreated,
    string? ErrorCode,
    string? Error);
