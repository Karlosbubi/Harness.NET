using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.SemanticIndex;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.SemanticIndex;

public sealed class SqliteSemanticIndexStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"harness-vector-store-{Guid.NewGuid():N}");

    [Fact]
    public async Task Rebuilds_compatible_partition_and_searches_only_current_generation()
    {
        (SqliteSemanticIndexStore store, string databasePath) = await CreateStoreAsync();
        SemanticIndexPartitionKey key = new(
            "workspace-1",
            new("TestProvider"),
            new("test-model"),
            new(2),
            new("test-v1"));
        SemanticIndexBuildHandle first = await store.BeginRebuildAsync(key);
        await store.AddAsync(first, [
            Chunk("first", "docs/first.md", [1f, 0f]),
            Chunk("second", "docs/second.md", [0f, 1f]),
        ]);
        SemanticIndexPartition firstPartition = await store.CompleteAsync(first, 2, 2);

        IReadOnlyList<SemanticVectorMatch> initial = await store.SearchAsync(key, [1f, 0f], 2);
        Assert.Equal("docs/first.md", initial[0].Path);

        SemanticIndexBuildHandle replacement = await store.BeginRebuildAsync(key);
        Assert.Equal(firstPartition.Id, (await store.GetCurrentAsync(key))?.Id);
        await store.AddAsync(replacement, [Chunk("replacement", "docs/new.md", [1f, 0f])]);
        SemanticIndexPartition current = await store.CompleteAsync(replacement, 1, 1);
        IReadOnlyList<SemanticVectorMatch> replaced = await store.SearchAsync(key, [1f, 0f], 10);

        Assert.Equal(replacement.Id, current.Id);
        Assert.Equal("docs/new.md", Assert.Single(replaced).Path);
        SemanticIndexPartitionKey incompatibleKey = key with
        {
            Model = new("other-model"),
        };
        SemanticIndexBuildHandle incompatible = await store.BeginRebuildAsync(incompatibleKey);
        await store.AddAsync(incompatible, [Chunk("other", "docs/other.md", [0f, 1f])]);
        await store.CompleteAsync(incompatible, 1, 1);
        Assert.Equal(replacement.Id, (await store.GetCurrentAsync(key))?.Id);
        Assert.Equal(incompatible.Id, (await store.GetCurrentAsync(incompatibleKey))?.Id);
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM semantic_index_partitions
            WHERE state = 'Superseded' AND id = $id;
            """;
        command.Parameters.AddWithValue("$id", firstPartition.Id);
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task Aborted_rebuild_does_not_replace_ready_partition()
    {
        (SqliteSemanticIndexStore store, _) = await CreateStoreAsync();
        SemanticIndexPartitionKey key = new(
            "workspace-1", new("Provider"), new("model"), new(2), new("v1"));
        SemanticIndexBuildHandle ready = await store.BeginRebuildAsync(key);
        await store.AddAsync(ready, [Chunk("ready", "ready.md", [1f, 0f])]);
        await store.CompleteAsync(ready, 1, 1);
        SemanticIndexBuildHandle interrupted = await store.BeginRebuildAsync(key);

        await store.AbortAsync(interrupted);

        Assert.Equal(ready.Id, (await store.GetCurrentAsync(key))?.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<(SqliteSemanticIndexStore Store, string DatabasePath)> CreateStoreAsync()
    {
        string databasePath = Path.Combine(root, "data", "harness.db");
        ApplicationPaths paths = new(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "state"),
            Path.Combine(root, "cache"),
            databasePath,
            Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspaces (
                id, root_path, name, entry_point, is_trusted, branch, is_dirty,
                created_at, updated_at, is_active)
            VALUES (
                'workspace-1', '/tmp/example', 'Example', '/tmp/example/Example.slnx',
                1, 'main', 0, $now, $now, 1);
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return (new(applicationPaths), databasePath);
    }

    private static SemanticChunkVector Chunk(string id, string path, float[] vector) =>
        new(id, path, 1, 1, id, $"hash-{id}", vector);

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
