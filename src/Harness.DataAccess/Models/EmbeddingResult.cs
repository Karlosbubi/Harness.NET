namespace Harness.DataAccess.Models;

public sealed record EmbeddingResult(
    IReadOnlyList<IReadOnlyList<float>> Embeddings,
    ProviderUsage Usage,
    ProviderError? Error);
