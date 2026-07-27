using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Inspection;

internal sealed class WorkspaceInspectionService(
    IWorkspaceStore workspaceStore,
    IWorkspaceFileReader fileReader,
    IWorkspaceTextSearcher textSearcher,
    IWorkspaceGitInspector gitInspector) : IWorkspaceInspectionService
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
                change.Status)).ToArray(),
            result.Diff,
            result.IsTruncated,
            result.ErrorCode,
            result.Error);
    }

    private static WorkspaceFileView Failure(string path, string code, string error) =>
        new(path, string.Empty, 0, IsTruncated: false, code, error);

    private static WorkspaceTextSearchView SearchFailure(string code, string error) =>
        new([], 0, IsTruncated: false, code, error);

    private static WorkspaceGitStateView GitFailure(string code, string error) =>
        new(string.Empty, null, [], string.Empty, IsTruncated: false, code, error);
}
