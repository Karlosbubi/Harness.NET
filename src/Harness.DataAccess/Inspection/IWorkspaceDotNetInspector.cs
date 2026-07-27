namespace Harness.DataAccess.Inspection;

public interface IWorkspaceDotNetInspector
{
    ValueTask<WorkspaceDotNetInfo> InspectAsync(
        string workspaceRoot,
        string entryPoint,
        CancellationToken cancellationToken = default);
}
