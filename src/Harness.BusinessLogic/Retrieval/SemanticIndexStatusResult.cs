namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticIndexStatusResult(
    SemanticIndexProfile Profile,
    SemanticIndexPartitionView? CurrentPartition,
    string? ErrorCode,
    string? Error);
