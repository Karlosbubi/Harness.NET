namespace Harness.BusinessLogic.Evidence;

public sealed record ToolEvidenceSnapshot(
    IReadOnlyList<ToolEvidenceView> Items,
    string? ErrorCode,
    string? Error);
