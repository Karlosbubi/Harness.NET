namespace Harness.DataAccess.Goals;

public interface IGoalModelSelectionStore
{
    ValueTask<StoredGoalModelSelection> SaveAsync(
        StoredGoalModelSelection selection,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredGoalModelSelection>> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default);
}
