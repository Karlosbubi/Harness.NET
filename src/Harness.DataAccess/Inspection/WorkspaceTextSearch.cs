namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceTextSearch(
    IReadOnlyList<WorkspaceTextMatch> Matches,
    int FilesScanned,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
