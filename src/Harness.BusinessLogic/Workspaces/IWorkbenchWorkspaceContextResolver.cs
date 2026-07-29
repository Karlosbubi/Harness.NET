namespace Harness.BusinessLogic.Workspaces;

internal interface IWorkbenchWorkspaceContextResolver
{
    ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default);
}
