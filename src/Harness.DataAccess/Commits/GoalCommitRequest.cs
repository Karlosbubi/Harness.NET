namespace Harness.DataAccess.Commits;

public sealed record GoalCommitRequest(
    GoalWorktreePath WorktreePath,
    GitBranchName ExpectedBranch,
    GitCommitSha ExpectedHead,
    GitDiffSha256 ExpectedDiffSha256,
    GitCommitMessage Message,
    GitAuthorName AuthorName,
    GitAuthorEmail AuthorEmail,
    DateTimeOffset CreatedAt);
