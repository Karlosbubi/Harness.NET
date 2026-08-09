namespace Harness.BusinessLogic.Inspection;

public sealed record WorkspaceFileView(
    string Path,
    string Content,
    string? Sha256,
    long SizeBytes,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
