using Harness.BusinessLogic.Costs;
using Harness.DataAccess.Models;
using Harness.DataAccess.SemanticIndex;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Retrieval;

internal sealed class SemanticIndexService(
    IWorkspaceStore workspaceStore,
    ITrackedTextCatalogReader catalogReader,
    ISemanticIndexStore indexStore,
    IModelProvider modelProvider,
    SemanticIndexOptions options) : ISemanticIndexService
{
    private const int MaximumQueryCharacters = 2_000;

    public async ValueTask<SemanticIndexResult> RebuildAsync(
        SemanticIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        RegisteredWorkspace? workspace = await GetTrustedWorkspaceAsync(
            request.WorkspaceId,
            cancellationToken);
        if (workspace is null)
        {
            return IndexFailure(
                "workspace_not_active_or_trusted",
                "The workspace must be active and trusted before semantic indexing.");
        }

        TrackedTextCatalog catalog = await catalogReader.ReadAsync(
            workspace.RootPath,
            cancellationToken);
        if (catalog.Error is not null)
        {
            return IndexFailure(catalog.ErrorCode ?? "catalog_failed", catalog.Error);
        }

        SemanticIndexPartitionKey partitionKey = PartitionKey(workspace.Id);
        SemanticIndexBuildHandle? build = null;
        int chunkCount = 0;
        int inputTokens = 0;
        long costMicrousd = 0;
        try
        {
            build = await indexStore.BeginRebuildAsync(partitionKey, cancellationToken);
            IReadOnlyList<SemanticTextChunk> chunks = catalog.Documents
                .SelectMany(SemanticTextChunker.Chunk)
                .ToArray();
            foreach (SemanticTextChunk[] batch in chunks.Chunk(options.EmbeddingBatchSize))
            {
                EmbeddingResult embedding = await modelProvider.EmbedAsync(new(
                    options.Model.Value,
                    batch.Select(chunk => chunk.Content).ToArray(),
                    options.Dimensions.Value,
                    Scope(request.RemoteGoalId, request.PrivacyPolicy)),
                    cancellationToken);
                if (embedding.Error is not null)
                {
                    await indexStore.AbortAsync(build, CancellationToken.None);
                    return IndexFailure(
                        embedding.Error.Code,
                        embedding.Error.Message,
                        catalog,
                        inputTokens,
                        costMicrousd);
                }

                if (embedding.Embeddings.Count != batch.Length ||
                    embedding.Embeddings.Any(vector => vector.Count != options.Dimensions.Value))
                {
                    await indexStore.AbortAsync(build, CancellationToken.None);
                    return IndexFailure(
                        "embedding_shape_mismatch",
                        "The embedding provider returned an incompatible vector shape.",
                        catalog,
                        inputTokens,
                        costMicrousd);
                }

                inputTokens += embedding.Usage.InputTokens;
                costMicrousd += embedding.Usage.Cost?.Value ?? 0;
                await indexStore.AddAsync(
                    build,
                    batch.Zip(embedding.Embeddings, (chunk, vector) => new SemanticChunkVector(
                        chunk.Id,
                        chunk.Path,
                        chunk.StartLine,
                        chunk.EndLine,
                        chunk.Content,
                        chunk.ContentHash,
                        vector)).ToArray(),
                    cancellationToken);
                chunkCount += batch.Length;
            }

            SemanticIndexPartition partition = await indexStore.CompleteAsync(
                build,
                catalog.Documents.Count,
                chunkCount,
                cancellationToken);
            return new(
                Map(partition),
                catalog.TrackedFileCount,
                catalog.SkippedFileCount,
                catalog.IsTruncated,
                Usage(inputTokens, costMicrousd),
                ErrorCode: null,
                Error: null);
        }
        catch (OperationCanceledException)
        {
            if (build is not null)
            {
                await indexStore.AbortAsync(build, CancellationToken.None);
            }

            throw;
        }
        catch (Exception exception)
        {
            if (build is not null)
            {
                await indexStore.AbortAsync(build, CancellationToken.None);
            }

            return IndexFailure(
                "index_failed",
                exception.Message,
                catalog,
                inputTokens,
                costMicrousd);
        }
    }

    public async ValueTask<SemanticSearchResult> SearchAsync(
        SemanticSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > MaximumQueryCharacters)
        {
            return SearchFailure(
                "invalid_query",
                $"The semantic query must contain 1-{MaximumQueryCharacters} characters.");
        }

        if (request.MaximumResults is < 1 or > 20)
        {
            return SearchFailure("invalid_limit", "The semantic result limit must be between 1 and 20.");
        }

        RegisteredWorkspace? workspace = await GetTrustedWorkspaceAsync(
            request.WorkspaceId,
            cancellationToken);
        if (workspace is null)
        {
            return SearchFailure(
                "workspace_not_active_or_trusted",
                "The workspace must be active and trusted before semantic retrieval.");
        }

        SemanticIndexPartitionKey partitionKey = PartitionKey(workspace.Id);
        SemanticIndexPartition? partition = await indexStore.GetCurrentAsync(
            partitionKey,
            cancellationToken);
        if (partition is null)
        {
            return SearchFailure(
                "index_missing",
                "No compatible semantic-index partition is ready.");
        }

        try
        {
            EmbeddingResult embedding = await modelProvider.EmbedAsync(new(
                options.Model.Value,
                [request.Query.Trim()],
                options.Dimensions.Value,
                Scope(request.RemoteGoalId, request.PrivacyPolicy)),
                cancellationToken);
            if (embedding.Error is not null)
            {
                return SearchFailure(embedding.Error.Code, embedding.Error.Message);
            }

            if (embedding.Embeddings.Count != 1 ||
                embedding.Embeddings[0].Count != options.Dimensions.Value)
            {
                return SearchFailure(
                    "embedding_shape_mismatch",
                    "The embedding provider returned an incompatible query vector.");
            }

            IReadOnlyList<SemanticVectorMatch> matches = await indexStore.SearchAsync(
                partitionKey,
                embedding.Embeddings[0],
                request.MaximumResults,
                cancellationToken);
            return new(
                Map(partition),
                matches.Select(match => new SemanticSearchMatchView(
                    match.Path,
                    match.StartLine,
                    match.EndLine,
                    match.Content,
                    new(match.Distance))).ToArray(),
                Usage(embedding.Usage.InputTokens, embedding.Usage.Cost?.Value ?? 0),
                ErrorCode: null,
                Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return SearchFailure("search_failed", exception.Message);
        }
    }

    private async ValueTask<RegisteredWorkspace?> GetTrustedWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        return workspace is not null &&
            workspace.Id.Equals(workspaceId, StringComparison.Ordinal) &&
            workspace.IsTrusted
                ? workspace
                : null;
    }

    private SemanticIndexPartitionKey PartitionKey(string workspaceId) => new(
        workspaceId,
        new(options.Provider.Value),
        new(options.Model.Value),
        new(options.Dimensions.Value),
        new(options.ChunkingVersion.Value));

    private static RemoteModelScope? Scope(
        string? remoteGoalId,
        SemanticPrivacyPolicy privacyPolicy) =>
        string.IsNullOrWhiteSpace(remoteGoalId)
            ? null
            : new(
                remoteGoalId,
                privacyPolicy is SemanticPrivacyPolicy.NoCollectionAndZeroDataRetention
                    ? ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention
                    : ProviderPrivacyPolicy.Normal);

    private static SemanticIndexPartitionView Map(SemanticIndexPartition partition) => new(
        partition.Id,
        new(partition.Key.Provider.Value),
        new(partition.Key.Model.Value),
        new(partition.Key.Dimensions.Value),
        new(partition.Key.ChunkingVersion.Value),
        partition.FileCount,
        partition.ChunkCount,
        partition.CompletedAt);

    private static EmbeddingUsageView Usage(int inputTokens, long costMicrousd) =>
        new(inputTokens, costMicrousd == 0 ? null : new MicroUsdAmount(costMicrousd));

    private static SemanticIndexResult IndexFailure(string code, string error) =>
        new(null, 0, 0, IsTruncated: false, Usage(0, 0), code, error);

    private static SemanticIndexResult IndexFailure(
        string code,
        string error,
        TrackedTextCatalog catalog,
        int inputTokens,
        long costMicrousd) => new(
            null,
            catalog.TrackedFileCount,
            catalog.SkippedFileCount,
            catalog.IsTruncated,
            Usage(inputTokens, costMicrousd),
            code,
            error);

    private static SemanticSearchResult SearchFailure(string code, string error) =>
        new(null, [], Usage(0, 0), code, error);
}
