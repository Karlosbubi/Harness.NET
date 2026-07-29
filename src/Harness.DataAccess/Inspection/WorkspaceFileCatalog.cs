namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceFileCatalog(
    IReadOnlyList<WorkspaceTrackedPath> Files,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
