namespace Harness.BusinessLogic.Goals;

public sealed record GoalWorktreeView(
    GoalId GoalId,
    string WorkspaceId,
    string Branch,
    string Path,
    string BaseCommit,
    GoalWorktreeState State,
    DateTimeOffset CreatedAt);
