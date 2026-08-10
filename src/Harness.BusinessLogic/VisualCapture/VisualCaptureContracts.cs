using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.VisualCapture;

public sealed record VisualCaptureId(string Value);
public sealed record VisualCaptureMaximumBytes(long Value);
public sealed record VisualCaptureRetentionDays(int Value);
public sealed record VisualCaptureMaximumPerGoal(int Value);
public sealed record VisualCaptureRelatedAction(string Value);
public sealed record VisualCaptureApplicationIdentity(string Value);
public sealed record VisualCaptureParentWindow(string Value);
public sealed record VisualCaptureUiScale(double Value);
public sealed record VisualCapturePixelSize(int Width, int Height);
public sealed record VisualCaptureSha256(string Value);
public sealed record VisualCaptureMediaType(string Value);
public sealed record VisualCaptureEncodedContent(string Base64);

public enum VisualCaptureInitiator
{
    Developer,
    Lead,
    Implementer,
    Reviewer,
}

public enum VisualCaptureTarget
{
    UserSelection,
    Screen,
    Window,
    Area,
    ActiveWindow,
}

public enum VisualCaptureOutcome
{
    Succeeded,
    Cancelled,
    Denied,
    PortalUnavailable,
    PortalFailed,
    Disabled,
    InvalidRequest,
    StaleRequest,
    InvalidImage,
    SizeRejected,
    StorageFailed,
    PolicyRejected,
    NotFound,
}

public enum VisualCaptureIdentityState
{
    Unavailable,
    ApplicationSupplied,
}

public enum VisualCaptureScaleState
{
    Unavailable,
    ApplicationSupplied,
}

public enum VisualCaptureModelAccess
{
    Local,
    Remote,
}

public sealed record VisualCapturePreferences(
    bool IsEnabled,
    VisualCaptureMaximumBytes MaximumBytes,
    VisualCaptureRetentionDays RetentionDays,
    VisualCaptureMaximumPerGoal MaximumPerGoal,
    bool AllowRemoteModelAccess)
{
    public static VisualCapturePreferences Default { get; } = new(
        true,
        new(5 * 1024 * 1024),
        new(7),
        new(20),
        false);
}

public sealed record VisualCaptureAvailability(
    bool IsAvailable,
    uint PortalVersion,
    IReadOnlyList<VisualCaptureTarget> AvailableTargets,
    string? ErrorCode,
    string? Error);

public sealed record VisualCaptureSettingsSnapshot(
    VisualCapturePreferences Preferences,
    VisualCaptureAvailability Availability,
    string PrivateStorageDescription);

public sealed record VisualCaptureSettingsResult(
    VisualCaptureSettingsSnapshot? Snapshot,
    string? ErrorCode,
    string? Error);

public sealed record VisualCaptureRequest(
    GoalId GoalId,
    ToolCorrelationId CorrelationId,
    VisualCaptureInitiator Initiator,
    VisualCaptureRelatedAction RelatedAction,
    VisualCaptureApplicationIdentity ApplicationIdentity,
    VisualCaptureTarget Target,
    DateTimeOffset RequestedAt,
    VisualCaptureParentWindow? ParentWindow = null,
    VisualCaptureUiScale? UiScale = null);

public sealed record VisualCaptureView(
    VisualCaptureId Id,
    GoalId GoalId,
    string WorkspaceId,
    VisualCaptureInitiator Initiator,
    VisualCaptureRelatedAction RelatedAction,
    VisualCaptureApplicationIdentity ApplicationIdentity,
    VisualCaptureTarget Target,
    VisualCaptureIdentityState IdentityState,
    string? WindowIdentity,
    string? DisplayIdentity,
    VisualCaptureScaleState ScaleState,
    VisualCaptureUiScale? UiScale,
    VisualCapturePixelSize PixelSize,
    VisualCaptureMediaType MediaType,
    VisualCaptureMaximumBytes Bytes,
    VisualCaptureSha256 Sha256,
    DateTimeOffset CreatedAt);

public sealed record VisualCaptureContentView(
    VisualCaptureView Capture,
    VisualCaptureEncodedContent Content);

public sealed record VisualCaptureResult(
    VisualCaptureOutcome Outcome,
    VisualCaptureView? Capture,
    string? ErrorCode,
    string? Error);

public sealed record VisualCaptureInspectionResult(
    VisualCaptureOutcome Outcome,
    VisualCaptureContentView? Content,
    string? ErrorCode,
    string? Error);

public sealed record VisualCaptureListResult(
    IReadOnlyList<VisualCaptureView> Captures,
    string? ErrorCode,
    string? Error);

public interface IVisualCaptureService
{
    ValueTask<VisualCaptureSettingsSnapshot> GetSettingsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<VisualCaptureSettingsResult> SaveSettingsAsync(
        VisualCapturePreferences preferences,
        CancellationToken cancellationToken = default);

    ValueTask<VisualCaptureResult> CaptureAsync(
        VisualCaptureRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<VisualCaptureListResult> ListAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default);

    ValueTask<VisualCaptureInspectionResult> InspectAsync(
        GoalId goalId,
        VisualCaptureId captureId,
        VisualCaptureModelAccess access,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        GoalId goalId,
        VisualCaptureId captureId,
        CancellationToken cancellationToken = default);

    ValueTask CleanupAsync(CancellationToken cancellationToken = default);
}
