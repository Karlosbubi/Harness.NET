namespace Harness.DataAccess.SemanticIndex;

public sealed record SemanticIndexPartitionKey(
    string WorkspaceId,
    EmbeddingProviderName Provider,
    EmbeddingModelName Model,
    VectorDimensionCount Dimensions,
    ChunkingVersion ChunkingVersion);
