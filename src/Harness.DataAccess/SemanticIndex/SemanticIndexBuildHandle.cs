namespace Harness.DataAccess.SemanticIndex;

public sealed record SemanticIndexBuildHandle(
    string Id,
    SemanticIndexPartitionKey Partition,
    string CollectionName);
