using Harness.DataAccess.Configuration;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Tools;
using Harness.DataAccess.Workspaces;

namespace Harness.DataAccess.Tests.Evidence;

public sealed class SqliteToolEvidenceStoreTests : IDisposable
{
    private const string ToolCallIdValue = "0123456789abcdef0123456789abcdef";
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-tool-evidence-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_one_correlated_call_and_completes_it_by_state_transition()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        string goalId = await CreateGoalAsync(paths);
        SqliteToolEvidenceStore store = new(paths);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        StoredToolCall requested = new(
            new(ToolCallIdValue),
            goalId,
            new("correlation-1"),
            ToolKind.FileEdit,
            "{\"path\":\"Program.cs\"}",
            ToolCallState.Running,
            ResultJson: null,
            startedAt,
            CompletedAt: null);

        StoredToolCallStart first = await store.StartAsync(requested);
        StoredToolCallStart duplicate = await store.StartAsync(requested with
        {
            Id = new("fedcba9876543210fedcba9876543210"),
        });
        StoredToolCall completed = await store.CompleteAsync(
            requested.Id,
            ToolCallState.Running,
            ToolCallState.Succeeded,
            "{\"newSha256\":\"abc\"}",
            startedAt.AddSeconds(1));
        IReadOnlyList<StoredToolCall> listed = await store.ListAsync(goalId);

        Assert.True(first.WasCreated);
        Assert.False(duplicate.WasCreated);
        Assert.Equal(requested.Id, duplicate.ToolCall.Id);
        Assert.Equal(ToolCallState.Succeeded, completed.State);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(completed, Assert.Single(listed));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteAsync(
            requested.Id,
            ToolCallState.Running,
            ToolCallState.Failed,
            "{}",
            startedAt.AddSeconds(2)).AsTask());
    }

    [Fact]
    public async Task Rejects_invalid_semantic_values_before_writing_evidence()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        string goalId = await CreateGoalAsync(paths);
        SqliteToolEvidenceStore store = new(paths);
        StoredToolCall invalid = new(
            new("not-an-id"),
            goalId,
            new("correlation"),
            (ToolKind)999,
            "{}",
            ToolCallState.Running,
            ResultJson: null,
            DateTimeOffset.UtcNow,
            CompletedAt: null);

        await Assert.ThrowsAsync<ArgumentException>(() => store.StartAsync(invalid).AsTask());
        Assert.Empty(await store.ListAsync(goalId));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async ValueTask<string> CreateGoalAsync(StubApplicationPaths paths)
    {
        string workspaceRoot = Path.Combine(root, "repository");
        string entryPoint = Path.Combine(workspaceRoot, "Repository.slnx");
        RegisteredWorkspace workspace = await new SqliteWorkspaceStore(paths).SaveAsync(
            new(workspaceRoot, "repository", "main", false, [entryPoint], Error: null),
            entryPoint);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StoredGoal goal = await new SqliteGoalStore(paths).CreateAsync(new(
            "goal-id",
            workspace.Id,
            "Goal",
            "Objective",
            3,
            null,
            "Draft",
            now,
            now));
        return goal.Id;
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(root, "config"),
        Path.Combine(root, "data"),
        Path.Combine(root, "state"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
