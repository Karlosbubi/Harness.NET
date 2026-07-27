namespace Harness.DataAccess.Workspaces;

public interface IWorkspaceInspector
{
    ValueTask<WorkspaceInspection> InspectAsync(
        string path,
        CancellationToken cancellationToken = default);
}
