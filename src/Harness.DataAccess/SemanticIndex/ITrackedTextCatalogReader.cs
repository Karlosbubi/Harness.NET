namespace Harness.DataAccess.SemanticIndex;

public interface ITrackedTextCatalogReader
{
    ValueTask<TrackedTextCatalog> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
