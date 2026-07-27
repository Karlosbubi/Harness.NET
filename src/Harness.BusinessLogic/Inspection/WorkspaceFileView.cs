namespace Harness.BusinessLogic.Inspection;

public sealed record WorkspaceFileView(
    string Path,
    string Content,
    long SizeBytes,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
