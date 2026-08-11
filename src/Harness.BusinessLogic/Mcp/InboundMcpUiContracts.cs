namespace Harness.BusinessLogic.Mcp;

public sealed record InboundUiActionId(string Value);
public sealed record InboundUiActionView(InboundUiActionId Id, string Name, bool IsAvailable);
public sealed record InboundUiElementView(
    string Id,
    string ControlType,
    string? AccessibleName,
    bool IsVisible,
    bool IsEnabled);
public sealed record InboundOpenDocumentView(
    string RelativePath,
    string? GoalId,
    string? Sha256,
    long BufferVersion,
    bool IsDirty,
    bool IsEditable,
    bool IsActive);
public sealed record InboundRenderedFrame(
    string MediaType,
    string Sha256,
    int PixelWidth,
    int PixelHeight,
    string Base64,
    bool IsTruncated,
    string? Error);
public sealed record InboundUiSnapshot(
    string Application,
    string WindowTitle,
    IReadOnlyList<InboundUiActionView> Actions,
    IReadOnlyList<InboundUiElementView> Elements,
    IReadOnlyList<InboundOpenDocumentView> OpenDocuments,
    InboundRenderedFrame? RenderedFrame,
    DateTimeOffset CapturedAt,
    string? Error);
public sealed record InboundUiActionResult(
    InboundUiActionId Action,
    bool WasApplied,
    string? ErrorCode,
    string? Error);
public sealed record InboundUiDocumentRequest(string RelativePath, string? GoalId);

public interface IInboundMcpUiBridge
{
    ValueTask<InboundUiSnapshot> InspectAsync(
        bool includeHarnessOwnedFrame,
        CancellationToken cancellationToken = default);
    ValueTask<InboundUiActionResult> ActivateAsync(
        InboundUiActionId action, CancellationToken cancellationToken = default);
    ValueTask<InboundUiActionResult> OpenDocumentAsync(
        InboundUiDocumentRequest request, CancellationToken cancellationToken = default);
}
