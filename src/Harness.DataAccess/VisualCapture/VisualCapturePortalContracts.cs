namespace Harness.DataAccess.VisualCapture;

public enum PortalCaptureState
{
    Succeeded,
    Cancelled,
    Denied,
    Unavailable,
    Failed,
}

public enum PortalCaptureTarget
{
    UserSelection,
    Screen,
    Window,
    Area,
    ActiveWindow,
}

public sealed record PortalParentWindowIdentifier(string Value);

public sealed record PortalCaptureRequest(
    PortalParentWindowIdentifier? ParentWindow,
    PortalCaptureTarget Target);

public sealed record PortalCaptureAvailability(
    bool IsAvailable,
    uint InterfaceVersion,
    uint AvailableTargets,
    string? ErrorCode,
    string? Error);

public sealed record PortalCaptureResult(
    PortalCaptureState State,
    Uri? ImageUri,
    uint InterfaceVersion,
    uint AvailableTargets,
    string? ErrorCode,
    string? Error);

public interface IVisualCapturePortal
{
    ValueTask<PortalCaptureAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);

    ValueTask<PortalCaptureResult> CaptureAsync(
        PortalCaptureRequest request,
        CancellationToken cancellationToken = default);
}

public enum PortalImageReadState
{
    Succeeded,
    InvalidUri,
    TooLarge,
    Missing,
    Failed,
}

public sealed record PortalImageReadResult(
    PortalImageReadState State,
    ReadOnlyMemory<byte> Content,
    string? ErrorCode,
    string? Error);

public interface IVisualCaptureImageSourceReader
{
    ValueTask<PortalImageReadResult> ReadAsync(
        Uri uri,
        long maximumBytes,
        CancellationToken cancellationToken = default);
}
