namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticIndexPartitionView(
    string Id,
    EmbeddingProvider Provider,
    EmbeddingModel Model,
    EmbeddingDimensions Dimensions,
    SemanticChunkingVersion ChunkingVersion,
    int FileCount,
    int ChunkCount,
    DateTimeOffset CompletedAt);
