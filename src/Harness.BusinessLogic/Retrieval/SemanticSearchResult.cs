namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticSearchResult(
    SemanticIndexPartitionView? Partition,
    IReadOnlyList<SemanticSearchMatchView> Matches,
    EmbeddingUsageView Usage,
    string? ErrorCode,
    string? Error);
