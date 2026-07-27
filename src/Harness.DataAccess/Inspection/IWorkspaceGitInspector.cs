namespace Harness.DataAccess.Inspection;

public interface IWorkspaceGitInspector
{
    ValueTask<WorkspaceGitState> InspectAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
