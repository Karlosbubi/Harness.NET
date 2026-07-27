using Harness.DataAccess.Configuration;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workspaces;

namespace Harness.DataAccess.Tests.Goals;

public sealed class SqliteGoalStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-goal-store-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Creates_reads_and_lists_goals_for_a_workspace()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        string workspaceRoot = Path.Combine(root, "repository");
        string entryPoint = Path.Combine(workspaceRoot, "Repository.slnx");
        SqliteWorkspaceStore workspaceStore = new(paths);
        RegisteredWorkspace workspace = await workspaceStore.SaveAsync(
            new(workspaceRoot, "repository", "main", false, [entryPoint], Error: null),
            entryPoint);
        SqliteGoalStore store = new(paths);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StoredGoal expected = new(
            "goal-id",
            workspace.Id,
            "Add inspection",
            "Implement a bounded inspection tool.",
            3,
            2_500_000,
            "Draft",
            now,
            now);

        StoredGoal created = await store.CreateAsync(expected);
        StoredGoal? loaded = await store.GetAsync(expected.Id);
        IReadOnlyList<StoredGoal> listed = await store.ListAsync(workspace.Id);

        Assert.Equal(expected.Id, created.Id);
        Assert.Equal(expected.Title, loaded?.Title);
        Assert.Equal(expected.RemoteBudgetMicrousd, loaded?.RemoteBudgetMicrousd);
        Assert.Equal(expected.Id, Assert.Single(listed).Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
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
