namespace Harness.BusinessLogic.Inspection;

public sealed record WorkspaceTextSearchView(
    IReadOnlyList<WorkspaceTextMatchView> Matches,
    int FilesScanned,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
