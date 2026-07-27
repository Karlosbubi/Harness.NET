namespace Harness.DataAccess.Inspection;

public interface IWorkspaceTextSearcher
{
    ValueTask<WorkspaceTextSearch> SearchAsync(
        string workspaceRoot,
        string query,
        CancellationToken cancellationToken = default);
}
