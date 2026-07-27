namespace Harness.DataAccess.Inspection;

public interface IWorkspaceFileReader
{
    ValueTask<WorkspaceFileRead> ReadAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken = default);
}
