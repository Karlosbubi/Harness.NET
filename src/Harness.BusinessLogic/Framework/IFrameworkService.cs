namespace Harness.BusinessLogic.Framework;

public interface IFrameworkService
{
    ValueTask<FrameworkSnapshot> GetEffectiveAsync(
        string workspaceId,
        string workspaceRoot,
        CancellationToken cancellationToken = default);

    ValueTask<FrameworkSnapshot> SetPrivateOverlayAsync(
        string workspaceId,
        string workspaceRoot,
        string? content,
        CancellationToken cancellationToken = default);
}
