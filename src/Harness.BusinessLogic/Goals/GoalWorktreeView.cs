namespace Harness.BusinessLogic.Goals;

public sealed record GoalWorktreeView(
    string GoalId,
    string WorkspaceId,
    string Branch,
    string Path,
    string BaseCommit,
    string State,
    DateTimeOffset CreatedAt);
