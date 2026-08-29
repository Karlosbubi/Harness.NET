using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Inspection;

internal sealed class WorkspaceInspectionService(
    IWorkspaceStore workspaceStore,
    IWorkspaceFileReader fileReader,
    IWorkspaceTextSearcher textSearcher,
    IWorkspaceGitInspector gitInspector,
    IWorkspaceDotNetInspector dotNetInspector) : IWorkspaceInspectionService
{
    public async ValueTask<WorkspaceFileView> ReadFileAsync(
        string workspaceId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(workspaceId, StringComparison.Ordinal))
        {
            return Failure(relativePath, "workspace_not_active", "The requested workspace is not active.");
        }

        if (!workspace.IsTrusted)
        {
            return Failure(relativePath, "workspace_not_trusted", "Trust the workspace before inspecting files.");
        }

        WorkspaceFileRead result = await fileReader.ReadAsync(
            workspace.RootPath,
            relativePath,
            cancellationToken);
        return new(
            result.Path,
            result.Content,
            result.Sha256,
            result.SizeBytes,
            result.IsTruncated,
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<WorkspaceTextSearchView> SearchTextAsync(
        string workspaceId,
        string query,
        CancellationToken cancellationToken = default)
    {
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(workspaceId, StringComparison.Ordinal))
        {
            return SearchFailure("workspace_not_active", "The requested workspace is not active.");
        }

        if (!workspace.IsTrusted)
        {
            return SearchFailure("workspace_not_trusted", "Trust the workspace before searching files.");
        }

        WorkspaceTextSearch result = await textSearcher.SearchAsync(
            workspace.RootPath,
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
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(workspaceId, StringComparison.Ordinal))
        {
            return GitFailure("workspace_not_active", "The requested workspace is not active.");
        }

        if (!workspace.IsTrusted)
        {
            return GitFailure("workspace_not_trusted", "Trust the workspace before inspecting Git state.");
        }

        WorkspaceGitState result = await gitInspector.InspectAsync(
            workspace.RootPath,
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
            result.UnstagedDiff,
            DeveloperGitService.MapPatchUnits(result.PatchUnits));
    }

    public async ValueTask<WorkspaceDotNetInfoView> InspectDotNetAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(workspaceId, StringComparison.Ordinal))
        {
            return DotNetFailure("workspace_not_active", "The requested workspace is not active.");
        }

        if (!workspace.IsTrusted)
        {
            return DotNetFailure("workspace_not_trusted", "Trust the workspace before inspecting .NET metadata.");
        }

        WorkspaceDotNetInfo result = await dotNetInspector.InspectAsync(
            workspace.RootPath,
            workspace.EntryPoint,
            cancellationToken);
        return DotNetInspectionMapper.Map(result);
    }

    private static WorkspaceFileView Failure(string path, string code, string error) =>
        new(path, string.Empty, Sha256: null, 0, IsTruncated: false, code, error);

    private static WorkspaceTextSearchView SearchFailure(string code, string error) =>
        new([], 0, IsTruncated: false, code, error);

    private static WorkspaceGitStateView GitFailure(string code, string error) =>
        new(string.Empty, null, [], string.Empty, IsTruncated: false, code, error);

    private static WorkspaceDotNetInfoView DotNetFailure(string code, string error) =>
        new(string.Empty, string.Empty, null, [], IsTruncated: false, code, error);
}
