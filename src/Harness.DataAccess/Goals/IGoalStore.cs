namespace Harness.DataAccess.Goals;

public interface IGoalStore
{
    ValueTask<StoredGoal> CreateAsync(
        StoredGoal goal,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoal?> GetAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredGoal>> ListAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);
}
