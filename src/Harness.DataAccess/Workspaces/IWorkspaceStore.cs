namespace Harness.DataAccess.Workspaces;

public interface IWorkspaceStore
{
    ValueTask<RegisteredWorkspace> SaveAsync(
        WorkspaceInspection inspection,
        string entryPoint,
        CancellationToken cancellationToken = default);

    ValueTask<RegisteredWorkspace?> FindByPathAsync(
        string rootPath,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<RegisteredWorkspace?> GetActiveAsync(
        CancellationToken cancellationToken = default);

    ValueTask<RegisteredWorkspace> SetActiveAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    ValueTask<RegisteredWorkspace> SetTrustAsync(
        string workspaceId,
        bool isTrusted,
        CancellationToken cancellationToken = default);
}
