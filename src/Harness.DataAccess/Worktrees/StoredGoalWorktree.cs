namespace Harness.DataAccess.Worktrees;

public sealed record StoredGoalWorktree(
    string GoalId,
    string WorkspaceId,
    string Branch,
    string Path,
    string BaseCommit,
    string State,
    DateTimeOffset CreatedAt);
