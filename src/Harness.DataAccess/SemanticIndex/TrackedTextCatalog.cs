namespace Harness.DataAccess.SemanticIndex;

public sealed record TrackedTextCatalog(
    IReadOnlyList<TrackedTextDocument> Documents,
    int TrackedFileCount,
    int SkippedFileCount,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
