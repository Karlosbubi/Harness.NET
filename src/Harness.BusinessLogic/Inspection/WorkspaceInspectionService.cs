using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Inspection;

internal sealed class WorkspaceInspectionService(
    IWorkspaceStore workspaceStore,
    IWorkspaceFileReader fileReader) : IWorkspaceInspectionService
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

    private static WorkspaceFileView Failure(string path, string code, string error) =>
        new(path, string.Empty, 0, IsTruncated: false, code, error);
}
