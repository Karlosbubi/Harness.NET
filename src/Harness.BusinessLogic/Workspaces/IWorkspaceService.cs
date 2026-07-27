namespace Harness.BusinessLogic.Workspaces;

public interface IWorkspaceService
{
    ValueTask<WorkspaceResult> InspectAsync(
        string path,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceResult> RegisterAsync(
        string path,
        string entryPoint,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceResult> SetTrustAsync(
        string workspaceId,
        bool isTrusted,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<WorkspaceView>> ListAsync(
        CancellationToken cancellationToken = default);
}
