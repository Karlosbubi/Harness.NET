using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Goals;

internal sealed class GoalService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore) : IGoalService
{
    private const int MaximumTitleCharacters = 160;
    private const int MaximumObjectiveCharacters = 16 * 1024;

    public async ValueTask<GoalResult> CreateAsync(
        GoalCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        string? validationError = Validate(request);
        if (validationError is not null)
        {
            return new(null, "invalid_goal", validationError);
        }

        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(request.WorkspaceId, StringComparison.Ordinal))
        {
            return new(null, "workspace_not_active", "The goal workspace must be active.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        StoredGoal stored = await goalStore.CreateAsync(new(
            Guid.NewGuid().ToString("N"),
            workspace.Id,
            request.Title.Trim(),
            request.Objective.Trim(),
            request.ReviewCycleLimit,
            request.RemoteBudgetMicrousd,
            "Draft",
            now,
            now), cancellationToken);
        return new(stored.ToView(), ErrorCode: null, Error: null);
    }

    public async ValueTask<GoalView?> GetAsync(
        string goalId,
        CancellationToken cancellationToken = default) =>
        (await goalStore.GetAsync(goalId, cancellationToken))?.ToView();

    public async ValueTask<IReadOnlyList<GoalView>> ListAsync(
        string workspaceId,
        CancellationToken cancellationToken = default) =>
        (await goalStore.ListAsync(workspaceId, cancellationToken))
        .Select(goal => goal.ToView())
        .ToArray();

    private static string? Validate(GoalCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return "A workspace is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > MaximumTitleCharacters)
        {
            return $"The title must contain 1-{MaximumTitleCharacters} characters.";
        }

        if (string.IsNullOrWhiteSpace(request.Objective) ||
            request.Objective.Length > MaximumObjectiveCharacters)
        {
            return $"The objective must contain 1-{MaximumObjectiveCharacters} characters.";
        }

        if (request.ReviewCycleLimit is < 1 or > 20)
        {
            return "The review-cycle limit must be between 1 and 20.";
        }

        return request.RemoteBudgetMicrousd is <= 0
            ? "The remote-model budget must be positive when provided."
            : null;
    }
}

internal static class StoredGoalMapping
{
    internal static GoalView ToView(this StoredGoal goal) => new(
        goal.Id,
        goal.WorkspaceId,
        goal.Title,
        goal.Objective,
        goal.ReviewCycleLimit,
        goal.RemoteBudgetMicrousd,
        goal.State,
        goal.CreatedAt,
        goal.UpdatedAt);
}
