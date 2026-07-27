namespace Harness.DataAccess.SemanticIndex;

public interface ISemanticIndexStore
{
    ValueTask<SemanticIndexBuildHandle> BeginRebuildAsync(
        SemanticIndexPartitionKey partition,
        CancellationToken cancellationToken = default);

    ValueTask AddAsync(
        SemanticIndexBuildHandle build,
        IReadOnlyList<SemanticChunkVector> chunks,
        CancellationToken cancellationToken = default);

    ValueTask<SemanticIndexPartition> CompleteAsync(
        SemanticIndexBuildHandle build,
        int fileCount,
        int chunkCount,
        CancellationToken cancellationToken = default);

    ValueTask AbortAsync(
        SemanticIndexBuildHandle build,
        CancellationToken cancellationToken = default);

    ValueTask<SemanticIndexPartition?> GetCurrentAsync(
        SemanticIndexPartitionKey partition,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SemanticVectorMatch>> SearchAsync(
        SemanticIndexPartitionKey partition,
        IReadOnlyList<float> queryVector,
        int maximumResults,
        CancellationToken cancellationToken = default);
}
