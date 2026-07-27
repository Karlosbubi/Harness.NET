namespace Harness.DataAccess.Framework;

public interface IFrameworkOverlayStore
{
    ValueTask<WorkspaceFrameworkOverlay?> GetAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceFrameworkOverlay> SaveAsync(
        string workspaceId,
        string content,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);
}
