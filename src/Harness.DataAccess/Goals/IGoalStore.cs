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

    ValueTask<StoredPlan?> GetCurrentPlanAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    ValueTask<StoredPlanSnapshot> SavePlanAsync(
        StoredPlan plan,
        string expectedGoalState,
        string nextGoalState,
        CancellationToken cancellationToken = default);

    ValueTask<StoredPlanSnapshot> DecidePlanAsync(
        StoredApproval approval,
        string expectedGoalState,
        string expectedPlanState,
        string nextGoalState,
        string nextPlanState,
        CancellationToken cancellationToken = default);
}
