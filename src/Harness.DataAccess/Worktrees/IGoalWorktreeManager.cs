namespace Harness.DataAccess.Worktrees;

public interface IGoalWorktreeManager
{
    ValueTask<GoalWorktreeResult> CreateAsync(
        string goalId,
        string repositoryRoot,
        CancellationToken cancellationToken = default);
}
