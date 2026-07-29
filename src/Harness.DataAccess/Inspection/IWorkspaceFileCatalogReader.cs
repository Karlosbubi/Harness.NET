namespace Harness.DataAccess.Inspection;

public interface IWorkspaceFileCatalogReader
{
    ValueTask<WorkspaceFileCatalog> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
