namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticIndexOptions(
    EmbeddingProvider Provider,
    EmbeddingModel Model,
    EmbeddingDimensions Dimensions,
    SemanticChunkingVersion ChunkingVersion,
    int EmbeddingBatchSize);
