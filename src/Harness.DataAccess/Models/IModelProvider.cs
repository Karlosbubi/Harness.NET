namespace Harness.DataAccess.Models;

public interface IModelProvider
{
    ValueTask<ModelCatalog> GetModelsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}
