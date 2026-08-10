namespace Harness.DataAccess.VisualCapture;

public sealed record StoredVisualCaptureId(string Value);

public enum StoredVisualCaptureTarget
{
    UserSelection,
    Screen,
    Window,
    Area,
    ActiveWindow,
}

public enum StoredVisualCaptureIdentityState
{
    Unavailable,
    ApplicationSupplied,
}

public enum StoredVisualCaptureScaleState
{
    Unavailable,
    ApplicationSupplied,
}

public sealed record StoredVisualCapture(
    StoredVisualCaptureId Id,
    string GoalId,
    string WorkspaceId,
    string Initiator,
    string RelatedAction,
    string ApplicationIdentity,
    StoredVisualCaptureTarget Target,
    StoredVisualCaptureIdentityState IdentityState,
    string? WindowIdentity,
    string? DisplayIdentity,
    StoredVisualCaptureScaleState ScaleState,
    double? UiScale,
    int PixelWidth,
    int PixelHeight,
    string MediaType,
    long Bytes,
    string Sha256,
    DateTimeOffset CreatedAt,
    string ArtifactFileName);

public sealed record StoredVisualCaptureWrite(
    StoredVisualCapture Capture,
    ReadOnlyMemory<byte> Content);

public sealed record StoredVisualCaptureContent(
    StoredVisualCapture Capture,
    ReadOnlyMemory<byte> Content);

public sealed record VisualCaptureRetentionPolicy(
    int RetentionDays,
    int MaximumCapturesPerGoal,
    DateTimeOffset Now);

public sealed record VisualCaptureCleanupResult(
    int RemovedCaptures,
    int RemovedTemporaryFiles,
    int RemovedInvalidArtifacts);

public interface IVisualCaptureArtifactStore
{
    ValueTask<StoredVisualCapture> StoreAsync(
        StoredVisualCaptureWrite write,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredVisualCapture>> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    ValueTask<StoredVisualCaptureContent?> ReadAsync(
        string goalId,
        StoredVisualCaptureId captureId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        string goalId,
        StoredVisualCaptureId captureId,
        CancellationToken cancellationToken = default);

    ValueTask<VisualCaptureCleanupResult> CleanupAsync(
        VisualCaptureRetentionPolicy policy,
        CancellationToken cancellationToken = default);
}
