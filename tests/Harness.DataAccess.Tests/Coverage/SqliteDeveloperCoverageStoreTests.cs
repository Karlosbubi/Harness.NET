using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Coverage;
using Harness.DataAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Coverage;

public sealed class SqliteDeveloperCoverageStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-coverage-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_latest_exact_context_provenance_and_line_hits()
    {
        StubPaths paths = new(Paths());
        DatabaseInitializationResult initialized =
            await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperCoverageStore store = new(paths);
        await store.SaveAsync(Import("original-old", goalId: null, "2026-08-29T10:00:00Z", 1));
        await store.SaveAsync(Import("goal-new", "goal-a", "2026-08-29T12:00:00Z", 3));
        await store.SaveAsync(Import("original-new", goalId: null, "2026-08-29T11:00:00Z", 7));

        StoredCoverageImport original = Assert.IsType<StoredCoverageImport>(
            await store.GetLatestAsync(new("workspace-a"), null));
        StoredCoverageImport goal = Assert.IsType<StoredCoverageImport>(
            await store.GetLatestAsync(new("workspace-a"), new("goal-a")));

        Assert.Equal(38, initialized.SchemaVersion.Value);
        Assert.Equal("original-new", original.Id.Value);
        Assert.Equal("src/Example.cs", Assert.Single(original.Lines).Path.Value);
        Assert.Equal(7, original.Lines[0].Hits.Value);
        Assert.Equal("goal-new", goal.Id.Value);
        Assert.Equal("Goal worktree", goal.SourceDescription.Value);
        Assert.Equal("coverlet", goal.Producer.Value);
        Assert.Equal("6.0.4", goal.ProducerVersion.Value);
        Assert.True(goal.IsTruncated);

        await using SqliteConnection connection = new($"Data Source={paths.Current.DatabasePath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON; " +
            "DELETE FROM developer_coverage_imports WHERE id = 'goal-new';");
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM developer_coverage_lines WHERE import_id = 'goal-new';"));
    }

    [Fact]
    public async Task Retains_only_ten_imports_per_exact_context()
    {
        StubPaths paths = new(Paths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteDeveloperCoverageStore store = new(paths);
        for (int index = 0; index < 12; index++)
        {
            await store.SaveAsync(Import(
                $"coverage-{index:D2}", goalId: null,
                DateTimeOffset.Parse("2026-08-29T10:00:00Z").AddMinutes(index).ToString("O"),
                index));
        }

        await using SqliteConnection connection = new($"Data Source={paths.Current.DatabasePath}");
        await connection.OpenAsync();
        Assert.Equal(10, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM developer_coverage_imports;"));
        Assert.Equal(10, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM developer_coverage_lines;"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM developer_coverage_imports WHERE id = 'coverage-00';"));
    }

    private static StoredCoverageImport Import(
        string id,
        string? goalId,
        string importedAt,
        long hits) => new(
        new(id), new("workspace-a"), goalId is null ? null : new(goalId),
        new(goalId is null ? "Original workspace" : "Goal worktree"),
        new("artifacts/coverage.xml"), new(new string('a', 64)),
        CoverageReportFormat.Cobertura, new("coverlet"), new("6.0.4"),
        DateTimeOffset.Parse("2026-08-29T09:00:00Z"), DateTimeOffset.Parse(importedAt),
        UnmappedFileCount: 2, IsTruncated: goalId is not null,
        [new(new("src/Example.cs"), new(12), new(hits))]);

    private ApplicationPaths Paths() => new(
        Path.Combine(root, "config"), Path.Combine(root, "data"),
        Path.Combine(root, "state"), Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class StubPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
