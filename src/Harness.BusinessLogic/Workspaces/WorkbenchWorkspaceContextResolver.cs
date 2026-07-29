using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Workspaces;

internal sealed class WorkbenchWorkspaceContextResolver(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore) : IWorkbenchWorkspaceContextResolver
{
    public async ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkspaceId is null || string.IsNullOrWhiteSpace(request.WorkspaceId.Value))
        {
            return Failure(request, "invalid_request", "A workspace is required.");
        }

        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null ||
            !workspace.Id.Equals(request.WorkspaceId.Value, StringComparison.Ordinal))
        {
            return Failure(
                request,
                "workspace_not_active",
                "The requested workspace is not active.");
        }

        if (!workspace.IsTrusted)
        {
            return Failure(
                request,
                "workspace_not_trusted",
                "Trust the workspace before inspecting its content.");
        }

        if (request.GoalId is not null)
        {
            StoredGoal? goal = await goalStore.GetAsync(request.GoalId.Value, cancellationToken);
            StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(
                request.GoalId.Value,
                cancellationToken);
            if (goal?.State == "Approved" && worktree?.State == "Active" &&
                goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal) &&
                worktree.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
            {
                return new(
                    new(
                        request.WorkspaceId,
                        request.GoalId,
                        new(worktree.Branch),
                        WorkbenchWorkspaceScope.ApprovedGoalWorktree,
                        $"Approved goal worktree · {worktree.Branch} · {goal.Title}"),
                    worktree.Path,
                    ErrorCode: null,
                    Error: null);
            }

            return new(
                new(
                    request.WorkspaceId,
                    null,
                    new(workspace.Branch),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace · selected goal has no active approved worktree"),
                workspace.RootPath,
                ErrorCode: null,
                Error: null);
        }

        return new(
            new(
                request.WorkspaceId,
                null,
                new(workspace.Branch),
                WorkbenchWorkspaceScope.OriginalWorkspace,
                "Original workspace · read-only source context"),
            workspace.RootPath,
            ErrorCode: null,
            Error: null);
    }

    private static WorkbenchWorkspaceResolution Failure(
        WorkbenchWorkspaceRequest request,
        string code,
        string error) => new(
        new(
            request.WorkspaceId ?? new(string.Empty),
            null,
            null,
            WorkbenchWorkspaceScope.Unavailable,
            "Workspace context unavailable"),
        RootPath: null,
        code,
        error);
}
