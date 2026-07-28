namespace Harness.DataAccess.Commits;

public sealed record GoalCommitInspectionRequest(
    GoalWorktreePath WorktreePath,
    GitBranchName ExpectedBranch);
