using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Inspection;

internal sealed class GoalWorkspaceInspectionService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IWorkspaceFileReader fileReader,
    IWorkspaceTextSearcher textSearcher,
    IWorkspaceGitInspector gitInspector,
    IWorkspaceDotNetInspector dotNetInspector) : IGoalWorkspaceInspectionService
{
    public async ValueTask<WorkspaceFileView> ReadFileAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        GoalInspectionContext? context = await ResolveAsync(goalId, scope, cancellationToken);
        if (context is null)
        {
            return new(relativePath, string.Empty, 0, false,
                "goal_workspace_unavailable", "The trusted goal workspace is unavailable.");
        }

        WorkspaceFileRead result = await fileReader.ReadAsync(
            context.RootPath,
            relativePath,
            cancellationToken);
        return new(result.Path, result.Content, result.SizeBytes, result.IsTruncated,
            result.ErrorCode, result.Error);
    }

    public async ValueTask<WorkspaceTextSearchView> SearchTextAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string query,
        CancellationToken cancellationToken = default)
    {
        GoalInspectionContext? context = await ResolveAsync(goalId, scope, cancellationToken);
        if (context is null)
        {
            return new([], 0, false, "goal_workspace_unavailable",
                "The trusted goal workspace is unavailable.");
        }

        WorkspaceTextSearch result = await textSearcher.SearchAsync(
            context.RootPath,
            query,
            cancellationToken);
        return new(
            result.Matches.Select(match => new WorkspaceTextMatchView(
                match.Path,
                match.LineNumber,
                match.Text)).ToArray(),
            result.FilesScanned,
            result.IsTruncated,
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<WorkspaceGitStateView> InspectGitAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        CancellationToken cancellationToken = default)
    {
        GoalInspectionContext? context = await ResolveAsync(goalId, scope, cancellationToken);
        if (context is null)
        {
            return new(string.Empty, null, [], string.Empty, false,
                "goal_workspace_unavailable", "The trusted goal workspace is unavailable.");
        }

        WorkspaceGitState result = await gitInspector.InspectAsync(
            context.RootPath,
            cancellationToken);
        return new(
            result.Branch,
            result.HeadSha,
            result.Changes.Select(change => new WorkspaceGitFileChangeView(
                change.Path,
                change.Status)).ToArray(),
            result.Diff,
            result.IsTruncated,
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<WorkspaceDotNetInfoView> InspectDotNetAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        CancellationToken cancellationToken = default)
    {
        GoalInspectionContext? context = await ResolveAsync(goalId, scope, cancellationToken);
        if (context is null)
        {
            return new(string.Empty, string.Empty, null, [], false,
                "goal_workspace_unavailable", "The trusted goal workspace is unavailable.");
        }

        WorkspaceDotNetInfo result = await dotNetInspector.InspectAsync(
            context.RootPath,
            context.EntryPoint,
            cancellationToken);
        return new(
            result.EntryPoint,
            result.EntryPointKind,
            result.SdkPolicy is null
                ? null
                : new(result.SdkPolicy.Version, result.SdkPolicy.RollForward,
                    result.SdkPolicy.AllowPrerelease),
            result.Projects.Select(project => new DotNetProjectView(
                project.Path,
                project.Sdk,
                project.TargetFrameworks,
                project.LanguageVersion,
                project.Nullable,
                project.References.Select(reference => new DotNetReferenceView(
                    reference.Kind,
                    reference.Identity,
                    reference.Version)).ToArray())).ToArray(),
            result.IsTruncated,
            result.ErrorCode,
            result.Error);
    }

    private async ValueTask<GoalInspectionContext?> ResolveAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        CancellationToken cancellationToken)
    {
        StoredGoal? goal = await goalStore.GetAsync(goalId.Value, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (goal is null || workspace is null || !workspace.IsTrusted ||
            !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return null;
        }

        string rootPath = workspace.RootPath;
        if (scope is GoalWorkspaceScope.ApprovedWorktree)
        {
            StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(
                goal.Id,
                cancellationToken);
            if (goal.State != "Approved" || worktree?.State != "Active" ||
                !worktree.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
            {
                return null;
            }

            rootPath = worktree.Path;
        }
        string relativeEntryPoint = Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint);
        return new(rootPath, Path.Combine(rootPath, relativeEntryPoint));
    }

    private sealed record GoalInspectionContext(string RootPath, string EntryPoint);
}
