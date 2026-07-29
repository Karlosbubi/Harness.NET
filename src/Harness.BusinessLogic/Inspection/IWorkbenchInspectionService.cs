using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Inspection;

public interface IWorkbenchInspectionService
{
    ValueTask<WorkbenchFileCatalogResult> ListFilesAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchTextSearchResult> SearchTextAsync(
        WorkbenchWorkspaceRequest request,
        string query,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchGitInspectionResult> InspectGitAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default);
}
