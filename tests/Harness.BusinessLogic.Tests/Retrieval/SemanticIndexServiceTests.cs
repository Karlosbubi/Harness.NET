using Harness.BusinessLogic.Retrieval;
using Harness.DataAccess.Models;
using Harness.DataAccess.SemanticIndex;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Tests.Retrieval;

public sealed class SemanticIndexServiceTests
{
    [Fact]
    public async Task Rebuilds_and_retrieves_with_compatible_semantic_partition()
    {
        RegisteredWorkspace workspace = Workspace(isTrusted: true);
        StubModelProvider provider = new();
        StubIndexStore store = new();
        SemanticIndexService service = Service(workspace, provider, store);

        SemanticIndexResult rebuilt = await service.RebuildAsync(new("workspace-1"));
        SemanticSearchResult search = await service.SearchAsync(new(
            "workspace-1",
            "alpha behavior",
            MaximumResults: 1));

        Assert.Null(rebuilt.Error);
        Assert.Equal("Fake", rebuilt.Partition?.Provider.Value);
        Assert.Equal("embedding-test", rebuilt.Partition?.Model.Value);
        Assert.Equal(2, rebuilt.Partition?.Dimensions.Value);
        Assert.Equal(2, rebuilt.Partition?.FileCount);
        Assert.True(rebuilt.Partition?.ChunkCount >= 2);
        Assert.Equal("src/Alpha.cs", Assert.Single(search.Matches).Path);
        Assert.All(provider.Requests, request => Assert.Equal(2, request.Dimensions));
    }

    [Fact]
    public async Task Passes_goal_scope_and_strict_privacy_to_remote_embeddings()
    {
        StubModelProvider provider = new();
        SemanticIndexService service = Service(
            Workspace(isTrusted: true),
            provider,
            new StubIndexStore());

        SemanticIndexResult result = await service.RebuildAsync(new(
            "workspace-1",
            "goal-1",
            SemanticPrivacyPolicy.NoCollectionAndZeroDataRetention));

        Assert.Null(result.Error);
        EmbeddingRequest request = Assert.Single(provider.Requests);
        Assert.Equal("goal-1", request.RemoteScope?.GoalId);
        Assert.Equal(
            ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention,
            request.RemoteScope?.PrivacyPolicy);
    }

    [Fact]
    public async Task Rejects_untrusted_workspace_before_reading_or_embedding()
    {
        StubModelProvider provider = new();
        StubCatalogReader catalog = new();
        SemanticIndexService service = new(
            new StubWorkspaceStore(Workspace(isTrusted: false)),
            catalog,
            new StubIndexStore(),
            provider,
            Options());

        SemanticIndexResult result = await service.RebuildAsync(new("workspace-1"));

        Assert.Equal("workspace_not_active_or_trusted", result.ErrorCode);
        Assert.Equal(0, catalog.ReadCount);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Keeps_previous_partition_when_embedding_shape_is_invalid()
    {
        StubModelProvider provider = new() { ReturnInvalidShape = true };
        StubIndexStore store = new();
        SemanticIndexService service = Service(Workspace(isTrusted: true), provider, store);

        SemanticIndexResult result = await service.RebuildAsync(new("workspace-1"));

        Assert.Equal("embedding_shape_mismatch", result.ErrorCode);
        Assert.True(store.WasAborted);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Chunking_is_deterministic_bounded_and_versionable()
    {
        string content = string.Join('\n', Enumerable.Range(1, 300).Select(index =>
            $"line {index}: {new string('x', 20)}"));
        TrackedTextDocument document = new("docs/long.md", content, "source-hash");

        IReadOnlyList<SemanticTextChunk> first = SemanticTextChunker.Chunk(document);
        IReadOnlyList<SemanticTextChunk> second = SemanticTextChunker.Chunk(document);

        Assert.Equal(first, second);
        Assert.True(first.Count > 1);
        Assert.All(first, chunk => Assert.InRange(chunk.Content.Length, 1, 1600));
        Assert.Equal(1, first[0].StartLine);
        Assert.True(first[^1].EndLine >= 299);
    }

    private static SemanticIndexService Service(
        RegisteredWorkspace workspace,
        StubModelProvider provider,
        StubIndexStore store) => new(
        new StubWorkspaceStore(workspace),
        new StubCatalogReader(),
        store,
        provider,
        Options());

    private static SemanticIndexOptions Options() => new(
        new("Fake"),
        new("embedding-test"),
        new(2),
        new("line-window-v1"),
        EmbeddingBatchSize: 16);

    private static RegisteredWorkspace Workspace(bool isTrusted) => new(
        "workspace-1",
        "/repo",
        "Repo",
        "/repo/Repo.slnx",
        isTrusted,
        IsActive: true,
        "main",
        IsDirty: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class StubCatalogReader : ITrackedTextCatalogReader
    {
        public int ReadCount { get; private set; }

        public ValueTask<TrackedTextCatalog> ReadAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ValueTask.FromResult(new TrackedTextCatalog(
                [
                    new("src/Alpha.cs", "alpha behavior", "hash-alpha"),
                    new("docs/Beta.md", "beta behavior", "hash-beta"),
                ],
                TrackedFileCount: 3,
                SkippedFileCount: 1,
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }
    }

    private sealed class StubModelProvider : IModelProvider
    {
        public List<EmbeddingRequest> Requests { get; } = [];

        public bool ReturnInvalidShape { get; init; }

        public ValueTask<ModelCatalog> GetModelsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            IReadOnlyList<IReadOnlyList<float>> vectors = ReturnInvalidShape
                ? [[1f]]
                : request.Inputs.Select(input =>
                    (IReadOnlyList<float>)(input.Contains("alpha", StringComparison.OrdinalIgnoreCase)
                        ? new float[] { 1f, 0f }
                        : new float[] { 0f, 1f })).ToArray();
            return ValueTask.FromResult(new EmbeddingResult(
                vectors,
                new(request.Inputs.Count, 0),
                Error: null));
        }
    }

    private sealed class StubIndexStore : ISemanticIndexStore
    {
        private readonly List<SemanticChunkVector> chunks = [];

        public SemanticIndexPartition? Current { get; private set; }

        public bool WasAborted { get; private set; }

        public ValueTask<SemanticIndexBuildHandle> BeginRebuildAsync(
            SemanticIndexPartitionKey partition,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SemanticIndexBuildHandle("build-1", partition, "collection-1"));

        public ValueTask AddAsync(
            SemanticIndexBuildHandle build,
            IReadOnlyList<SemanticChunkVector> values,
            CancellationToken cancellationToken = default)
        {
            chunks.AddRange(values);
            return ValueTask.CompletedTask;
        }

        public ValueTask<SemanticIndexPartition> CompleteAsync(
            SemanticIndexBuildHandle build,
            int fileCount,
            int chunkCount,
            CancellationToken cancellationToken = default)
        {
            Current = new(
                build.Id,
                build.Partition,
                fileCount,
                chunkCount,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            return ValueTask.FromResult(Current);
        }

        public ValueTask AbortAsync(
            SemanticIndexBuildHandle build,
            CancellationToken cancellationToken = default)
        {
            WasAborted = true;
            chunks.Clear();
            return ValueTask.CompletedTask;
        }

        public ValueTask<SemanticIndexPartition?> GetCurrentAsync(
            SemanticIndexPartitionKey partition,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);

        public ValueTask<IReadOnlyList<SemanticVectorMatch>> SearchAsync(
            SemanticIndexPartitionKey partition,
            IReadOnlyList<float> queryVector,
            int maximumResults,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SemanticVectorMatch>>(
            chunks.OrderByDescending(chunk =>
                    chunk.Vector.Zip(queryVector, (left, right) => left * right).Sum())
                .Take(maximumResults)
                .Select(chunk => new SemanticVectorMatch(
                    chunk.Id,
                    chunk.Path,
                    chunk.StartLine,
                    chunk.EndLine,
                    chunk.Content,
                    chunk.ContentHash,
                    Distance: 0))
                .ToArray());
    }

    private sealed class StubWorkspaceStore(RegisteredWorkspace workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(workspace);

        public ValueTask<RegisteredWorkspace> SaveAsync(
            WorkspaceInspection inspection,
            string entryPoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace?> FindByPathAsync(
            string rootPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetActiveAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetTrustAsync(
            string workspaceId,
            bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
