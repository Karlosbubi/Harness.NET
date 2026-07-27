namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceFileRead(
    string Path,
    string Content,
    long SizeBytes,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
