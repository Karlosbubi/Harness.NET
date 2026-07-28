namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticIndexProfile(
    EmbeddingProvider Provider,
    EmbeddingModel Model,
    EmbeddingDimensions Dimensions,
    SemanticChunkingVersion ChunkingVersion,
    EmbeddingAccess Access);
