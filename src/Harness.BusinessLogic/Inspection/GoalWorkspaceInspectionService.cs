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
    IWorkspaceDotNetInspector dotNetInspector,
    IWorkspaceAdvancedInspector advancedInspector) : IGoalWorkspaceInspectionService
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
            return new(relativePath, string.Empty, Sha256: null, 0, false,
                "goal_workspace_unavailable", "The trusted goal workspace is unavailable.");
        }

        WorkspaceFileRead result = await fileReader.ReadAsync(
            context.RootPath,
            relativePath,
            cancellationToken);
        return new(result.Path, result.Content, result.Sha256, result.SizeBytes, result.IsTruncated,
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
                change.Status,
                change.IndexStatus,
                change.WorktreeStatus,
                change.IsStaged,
                change.IsUnstaged,
                change.IsConflicted)).ToArray(),
            result.Diff,
            result.IsTruncated,
            result.ErrorCode,
            result.Error,
            result.Fingerprint,
            result.StagedDiff,
            result.UnstagedDiff);
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

    public async ValueTask<GoalTreeView> ListTreeAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string relativeRoot,
        string? glob,
        int maximumDepth,
        int maximumResults,
        string? continuation,
        CancellationToken cancellationToken = default)
    {
        GoalInspectionContext? context = await ResolveAsync(goalId, scope, cancellationToken);
        if (context is null)
        {
            return new(null, [], null, false, "goal_workspace_unavailable",
                "The trusted goal workspace is unavailable.");
        }

        WorkspaceTreeResult result = await advancedInspector.ListTreeAsync(
            context.RootPath,
            new(
                new(relativeRoot),
                string.IsNullOrWhiteSpace(glob) ? null : new(glob),
                maximumDepth,
                maximumResults,
                string.IsNullOrWhiteSpace(continuation) ? null : new(continuation)),
            cancellationToken);
        return new(
            Identity(context),
            result.Entries.Select(entry => new GoalTreeEntryView(
                entry.Path.Value, entry.Kind.ToString(), entry.Depth)).ToArray(),
            result.Continuation?.Value,
            result.IsTruncated,
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<GoalFileRangeView> ReadRangeAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string relativePath,
        int startLine,
        int lineCount,
        CancellationToken cancellationToken = default)
    {
        GoalInspectionContext? context = await ResolveAsync(goalId, scope, cancellationToken);
        if (context is null)
        {
            return new(null, relativePath, 0, 0, 0, string.Empty, null, false,
                "goal_workspace_unavailable", "The trusted goal workspace is unavailable.");
        }

        WorkspaceRangeResult result = await advancedInspector.ReadRangeAsync(
            context.RootPath,
            new(new(relativePath), startLine, lineCount),
            cancellationToken);
        return new(
            Identity(context),
            result.Path.Value,
            result.StartLine,
            result.EndLine,
            result.TotalLines,
            result.Content,
            result.Sha256,
            result.IsTruncated,
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<GoalRegexSearchView> SearchRegexAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string pattern,
        string? fileGlob,
        int maximumResults,
        string? continuation,
        CancellationToken cancellationToken = default)
    {
        GoalInspectionContext? context = await ResolveAsync(goalId, scope, cancellationToken);
        if (context is null)
        {
            return new(null, [], 0, null, false, "goal_workspace_unavailable",
                "The trusted goal workspace is unavailable.");
        }

        WorkspaceRegexResult result = await advancedInspector.SearchRegexAsync(
            context.RootPath,
            new(
                new(pattern),
                string.IsNullOrWhiteSpace(fileGlob) ? null : new(fileGlob),
                maximumResults,
                string.IsNullOrWhiteSpace(continuation) ? null : new(continuation)),
            cancellationToken);
        return new(
            Identity(context),
            result.Matches.Select(match => new GoalRegexMatchView(
                match.Path.Value, match.Line, match.Character, match.Length, match.Text)).ToArray(),
            result.FilesScanned,
            result.Continuation?.Value,
            result.IsTruncated,
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<GoalProjectGraphView> InspectProjectGraphAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        CancellationToken cancellationToken = default)
    {
        GoalInspectionContext? context = await ResolveAsync(goalId, scope, cancellationToken);
        if (context is null)
        {
            return new(null, [], [], false, "goal_workspace_unavailable",
                "The trusted goal workspace is unavailable.");
        }

        WorkspaceDotNetInfo result = await dotNetInspector.InspectAsync(
            context.RootPath, context.EntryPoint, cancellationToken);
        DotNetProjectView[] projects = result.Projects.Select(project => new DotNetProjectView(
            project.Path,
            project.Sdk,
            project.TargetFrameworks,
            project.LanguageVersion,
            project.Nullable,
            project.References.Select(reference => new DotNetReferenceView(
                reference.Kind, reference.Identity, reference.Version)).ToArray())).ToArray();
        GoalProjectDependencyView[] dependencies = projects
            .SelectMany(project => project.References
                .Where(reference => reference.Kind.Equals("Project", StringComparison.OrdinalIgnoreCase))
                .Select(reference => new GoalProjectDependencyView(project.Path, reference.Identity)))
            .ToArray();
        return new(Identity(context), projects, dependencies, result.IsTruncated,
            result.ErrorCode, result.Error);
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
        string branch = workspace.Branch;
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
            branch = worktree.Branch;
        }
        string relativeEntryPoint = Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint);
        return new(
            rootPath,
            Path.Combine(rootPath, relativeEntryPoint),
            workspace.Id,
            goal.Id,
            scope,
            branch,
            relativeEntryPoint);
    }

    private static GoalInspectionIdentity Identity(GoalInspectionContext context) => new(
        new($"{context.WorkspaceId}:{context.GoalId}:{context.Scope}:{context.Branch}:{context.RelativeEntryPoint}"),
        context.WorkspaceId,
        context.GoalId,
        context.Scope,
        context.Branch,
        context.RelativeEntryPoint);

    private sealed record GoalInspectionContext(
        string RootPath,
        string EntryPoint,
        string WorkspaceId,
        string GoalId,
        GoalWorkspaceScope Scope,
        string Branch,
        string RelativeEntryPoint);
}
