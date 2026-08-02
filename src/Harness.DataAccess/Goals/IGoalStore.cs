using Harness.DataAccess.Worktrees;

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

    ValueTask<StoredGoal?> UpdateDraftSettingsAsync(
        string goalId,
        DateTimeOffset expectedUpdatedAt,
        int reviewCycleLimit,
        long? remoteBudgetMicrousd,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(
        string extensionId,
        string goalId,
        long? expectedBudgetMicrousd,
        long newBudgetMicrousd,
        string reason,
        DateTimeOffset approvedAt,
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
        StoredGoalWorktree? worktree,
        string expectedGoalState,
        string expectedPlanState,
        string nextGoalState,
        string nextPlanState,
        CancellationToken cancellationToken = default);

    ValueTask<StoredGoalWorktree?> GetWorktreeAsync(
        string goalId,
        CancellationToken cancellationToken = default);
}
