namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticIndexResult(
    SemanticIndexPartitionView? Partition,
    int TrackedFileCount,
    int SkippedFileCount,
    bool IsTruncated,
    EmbeddingUsageView Usage,
    string? ErrorCode,
    string? Error);
