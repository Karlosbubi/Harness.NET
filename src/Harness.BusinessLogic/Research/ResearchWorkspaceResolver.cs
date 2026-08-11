using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Research;

internal sealed class ResearchWorkspaceResolver(
    IWorkspaceStore workspaceStore,
    IGoalStore goalStore)
{
    internal async ValueTask<ResearchWorkspaceContext?> ResolveAsync(
        GoalId? goalId,
        DependencyInspectionScope scope,
        CancellationToken cancellationToken)
    {
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.IsTrusted)
        {
            return null;
        }
        if (goalId is null)
        {
            return new(workspace.RootPath, workspace.EntryPoint);
        }
        StoredGoal? goal = await goalStore.GetAsync(goalId.Value, cancellationToken);
        if (goal is null || !goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
        {
            return null;
        }
        if (scope is DependencyInspectionScope.Original)
        {
            return new(workspace.RootPath, workspace.EntryPoint);
        }
        StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(goal.Id, cancellationToken);
        if (goal.State != "Approved" || worktree?.State != "Active" ||
            !worktree.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
        {
            return null;
        }
        string relativeEntry = Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint);
        return new(worktree.Path, Path.Combine(worktree.Path, relativeEntry));
    }
}

internal sealed record ResearchWorkspaceContext(string RootPath, string EntryPoint);
