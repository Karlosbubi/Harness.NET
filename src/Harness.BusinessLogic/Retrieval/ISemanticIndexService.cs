namespace Harness.BusinessLogic.Retrieval;

public interface ISemanticIndexService
{
    ValueTask<SemanticIndexStatusResult> GetStatusAsync(
        SemanticIndexRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<SemanticIndexResult> RebuildAsync(
        SemanticIndexRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<SemanticSearchResult> SearchAsync(
        SemanticSearchRequest request,
        CancellationToken cancellationToken = default);
}
