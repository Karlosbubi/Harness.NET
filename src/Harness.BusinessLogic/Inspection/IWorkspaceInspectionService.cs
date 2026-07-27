namespace Harness.BusinessLogic.Inspection;

public interface IWorkspaceInspectionService
{
    ValueTask<WorkspaceFileView> ReadFileAsync(
        string workspaceId,
        string relativePath,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceTextSearchView> SearchTextAsync(
        string workspaceId,
        string query,
        CancellationToken cancellationToken = default);
}
