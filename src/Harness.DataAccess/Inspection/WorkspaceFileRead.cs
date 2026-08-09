namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceFileRead(
    string Path,
    string Content,
    string? Sha256,
    long SizeBytes,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
