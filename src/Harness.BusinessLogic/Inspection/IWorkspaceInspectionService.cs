namespace Harness.BusinessLogic.Inspection;

public interface IWorkspaceInspectionService
{
    ValueTask<WorkspaceFileView> ReadFileAsync(
        string workspaceId,
        string relativePath,
        CancellationToken cancellationToken = default);
}
