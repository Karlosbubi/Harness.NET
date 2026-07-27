using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;

namespace Harness.DataAccess.SemanticIndex;

internal sealed class SqliteSemanticIndexStore(IApplicationPaths applicationPaths)
    : ISemanticIndexStore
{
    public async ValueTask<SemanticIndexBuildHandle> BeginRebuildAsync(
        SemanticIndexPartitionKey partition,
        CancellationToken cancellationToken = default)
    {
        Validate(partition);
        string id = Guid.NewGuid().ToString("N");
        string collectionName = $"semantic_chunks_{id}";
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO semantic_index_partitions (
                id, workspace_id, provider, model, dimensions, chunking_version,
                collection_name, state, file_count, chunk_count, created_at, completed_at)
            VALUES (
                @id, @workspaceId, @provider, @model, @dimensions, @chunkingVersion,
                @collectionName, 'Building', 0, 0, @createdAt, NULL);
            """, new
        {
            id,
            workspaceId = partition.WorkspaceId,
            provider = partition.Provider.Value,
            model = partition.Model.Value,
            dimensions = partition.Dimensions.Value,
            chunkingVersion = partition.ChunkingVersion.Value,
            collectionName,
            createdAt = Format(createdAt),
        }, cancellationToken: cancellationToken));

        SemanticIndexBuildHandle build = new(id, partition, collectionName);
        try
        {
            await Collection(build).EnsureCollectionExistsAsync(cancellationToken);
            return build;
        }
        catch
        {
            await MarkFailedAsync(id, CancellationToken.None);
            throw;
        }
    }

    public async ValueTask AddAsync(
        SemanticIndexBuildHandle build,
        IReadOnlyList<SemanticChunkVector> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        int dimensions = build.Partition.Dimensions.Value;
        if (chunks.Any(chunk => chunk.Vector.Count != dimensions))
        {
            throw new ArgumentException(
                "Every chunk vector must match the partition dimensions.",
                nameof(chunks));
        }

        SqliteCollection<string, SqliteSemanticChunk> collection = Collection(build);
        await collection.UpsertAsync(chunks.Select(chunk => new SqliteSemanticChunk
        {
            Id = chunk.Id,
            Path = chunk.Path,
            StartLine = chunk.StartLine,
            EndLine = chunk.EndLine,
            Content = chunk.Content,
            ContentHash = chunk.ContentHash,
            Vector = chunk.Vector.ToArray(),
        }), cancellationToken);
    }

    public async ValueTask<SemanticIndexPartition> CompleteAsync(
        SemanticIndexBuildHandle build,
        int fileCount,
        int chunkCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileCount);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE semantic_index_partitions
            SET state = 'Superseded'
            WHERE workspace_id = @workspaceId
              AND provider = @provider
              AND model = @model
              AND dimensions = @dimensions
              AND chunking_version = @chunkingVersion
              AND state = 'Ready';
            """, Parameters(build), transaction, cancellationToken: cancellationToken));
        int updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE semantic_index_partitions
            SET state = 'Ready', file_count = @fileCount, chunk_count = @chunkCount,
                completed_at = @completedAt
            WHERE id = @id AND state = 'Building';
            """, new
        {
            id = build.Id,
            fileCount = fileCount,
            chunkCount = chunkCount,
            completedAt = Format(completedAt),
        }, transaction, cancellationToken: cancellationToken));
        if (updated != 1)
        {
            throw new InvalidOperationException("The semantic-index build is not active.");
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetCurrentAsync(build.Partition, cancellationToken) ??
            throw new InvalidOperationException("The completed semantic-index partition is unavailable.");
    }

    public async ValueTask AbortAsync(
        SemanticIndexBuildHandle build,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Collection(build).EnsureCollectionDeletedAsync(cancellationToken);
        }
        finally
        {
            await MarkFailedAsync(build.Id, CancellationToken.None);
        }
    }

    public async ValueTask<SemanticIndexPartition?> GetCurrentAsync(
        SemanticIndexPartitionKey partition,
        CancellationToken cancellationToken = default)
    {
        Validate(partition);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        PartitionRow? row = await connection.QuerySingleOrDefaultAsync<PartitionRow>(
            new CommandDefinition(SelectCurrentSql, new
            {
                workspaceId = partition.WorkspaceId,
                provider = partition.Provider.Value,
                model = partition.Model.Value,
                dimensions = partition.Dimensions.Value,
                chunkingVersion = partition.ChunkingVersion.Value,
            }, cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<IReadOnlyList<SemanticVectorMatch>> SearchAsync(
        SemanticIndexPartitionKey partition,
        IReadOnlyList<float> queryVector,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        if (maximumResults is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }

        if (queryVector.Count != partition.Dimensions.Value)
        {
            throw new ArgumentException(
                "The query vector must match the partition dimensions.",
                nameof(queryVector));
        }

        SemanticIndexPartition? current = await GetCurrentAsync(partition, cancellationToken);
        if (current is null)
        {
            return [];
        }

        string collectionName = await GetCollectionNameAsync(current.Id, cancellationToken);
        SqliteCollection<string, SqliteSemanticChunk> collection = Collection(
            new(current.Id, current.Key, collectionName));
        List<SemanticVectorMatch> matches = [];
        ReadOnlyMemory<float> vector = queryVector.ToArray();
        await foreach (VectorSearchResult<SqliteSemanticChunk> result in collection.SearchAsync(
                           vector,
                           maximumResults,
                           cancellationToken: cancellationToken))
        {
            SqliteSemanticChunk record = result.Record;
            matches.Add(new(
                record.Id,
                record.Path,
                record.StartLine,
                record.EndLine,
                record.Content,
                record.ContentHash,
                result.Score ?? double.NaN));
        }

        return matches;
    }

    private SqliteCollection<string, SqliteSemanticChunk> Collection(
        SemanticIndexBuildHandle build) => new(
        ConnectionString,
        build.CollectionName,
        new SqliteCollectionOptions
        {
            Definition = Definition(build.Partition.Dimensions.Value),
        });

    private static VectorStoreCollectionDefinition Definition(int dimensions) => new()
    {
        Properties =
        [
            new VectorStoreKeyProperty(nameof(SqliteSemanticChunk.Id), typeof(string)),
            new VectorStoreDataProperty(nameof(SqliteSemanticChunk.Path), typeof(string)),
            new VectorStoreDataProperty(nameof(SqliteSemanticChunk.StartLine), typeof(int)),
            new VectorStoreDataProperty(nameof(SqliteSemanticChunk.EndLine), typeof(int)),
            new VectorStoreDataProperty(nameof(SqliteSemanticChunk.Content), typeof(string)),
            new VectorStoreDataProperty(nameof(SqliteSemanticChunk.ContentHash), typeof(string)),
            new VectorStoreVectorProperty(
                nameof(SqliteSemanticChunk.Vector),
                typeof(ReadOnlyMemory<float>),
                dimensions)
            {
                DistanceFunction = DistanceFunction.CosineDistance,
            },
        ],
    };

    private async ValueTask<string> GetCollectionNameAsync(
        string partitionId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT collection_name FROM semantic_index_partitions WHERE id = @partitionId;
            """, new { partitionId }, cancellationToken: cancellationToken));
    }

    private async ValueTask MarkFailedAsync(string id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE semantic_index_partitions
            SET state = 'Failed', completed_at = @completedAt
            WHERE id = @id AND state = 'Building';
            """, new { id, completedAt = Format(DateTimeOffset.UtcNow) },
            cancellationToken: cancellationToken));
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = applicationPaths.Current.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true,
    }.ToString();

    private static object Parameters(SemanticIndexBuildHandle build) => new
    {
        workspaceId = build.Partition.WorkspaceId,
        provider = build.Partition.Provider.Value,
        model = build.Partition.Model.Value,
        dimensions = build.Partition.Dimensions.Value,
        chunkingVersion = build.Partition.ChunkingVersion.Value,
    };

    private static void Validate(SemanticIndexPartitionKey partition)
    {
        if (string.IsNullOrWhiteSpace(partition.WorkspaceId) ||
            string.IsNullOrWhiteSpace(partition.Provider.Value) ||
            string.IsNullOrWhiteSpace(partition.Model.Value) ||
            partition.Dimensions.Value <= 0 ||
            string.IsNullOrWhiteSpace(partition.ChunkingVersion.Value))
        {
            throw new ArgumentException("The semantic-index partition is invalid.", nameof(partition));
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private const string SelectCurrentSql = """
        SELECT id, workspace_id AS WorkspaceId, provider, model, dimensions,
               chunking_version AS ChunkingVersion, file_count AS FileCount,
               chunk_count AS ChunkCount, created_at AS CreatedAt,
               completed_at AS CompletedAt
        FROM semantic_index_partitions
        WHERE workspace_id = @workspaceId
          AND provider = @provider
          AND model = @model
          AND dimensions = @dimensions
          AND chunking_version = @chunkingVersion
          AND state = 'Ready';
        """;

    private sealed record SqliteSemanticChunk
    {
        public string Id { get; init; } = string.Empty;

        public string Path { get; init; } = string.Empty;

        public int StartLine { get; init; }

        public int EndLine { get; init; }

        public string Content { get; init; } = string.Empty;

        public string ContentHash { get; init; } = string.Empty;

        public ReadOnlyMemory<float> Vector { get; init; }
    }

    private sealed class PartitionRow
    {
        public string Id { get; init; } = string.Empty;

        public string WorkspaceId { get; init; } = string.Empty;

        public string Provider { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public int Dimensions { get; init; }

        public string ChunkingVersion { get; init; } = string.Empty;

        public int FileCount { get; init; }

        public int ChunkCount { get; init; }

        public string CreatedAt { get; init; } = string.Empty;

        public string CompletedAt { get; init; } = string.Empty;

        public SemanticIndexPartition ToRecord() => new(
            Id,
            new(
                WorkspaceId,
                new(Provider),
                new(Model),
                new(Dimensions),
                new(ChunkingVersion)),
            FileCount,
            ChunkCount,
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(CompletedAt, CultureInfo.InvariantCulture));
    }
}
