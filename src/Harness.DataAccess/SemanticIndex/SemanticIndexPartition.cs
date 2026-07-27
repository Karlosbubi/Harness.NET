namespace Harness.DataAccess.SemanticIndex;

public sealed record SemanticIndexPartition(
    string Id,
    SemanticIndexPartitionKey Key,
    int FileCount,
    int ChunkCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt);
