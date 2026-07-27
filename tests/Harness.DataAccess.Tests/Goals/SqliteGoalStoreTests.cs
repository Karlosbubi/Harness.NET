using Harness.DataAccess.Configuration;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using Microsoft.Data.Sqlite;

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

    [Fact]
    public async Task Saves_plan_revisions_and_decisions_atomically()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        string workspaceRoot = Path.Combine(root, "repository");
        string entryPoint = Path.Combine(workspaceRoot, "Repository.slnx");
        RegisteredWorkspace workspace = await new SqliteWorkspaceStore(paths).SaveAsync(
            new(workspaceRoot, "repository", "main", false, [entryPoint], Error: null),
            entryPoint);
        SqliteGoalStore store = new(paths);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StoredGoal goal = await store.CreateAsync(new(
            "goal-id",
            workspace.Id,
            "Goal",
            "Objective",
            3,
            null,
            "Draft",
            now,
            now));
        StoredPlan plan = new(
            "plan-id",
            goal.Id,
            1,
            "Implement and test.",
            "Pending",
            now,
            now);

        StoredPlanSnapshot proposed = await store.SavePlanAsync(
            plan,
            "Draft",
            "AwaitingPlanApproval");
        StoredGoalWorktree worktree = new(
            goal.Id,
            workspace.Id,
            "harness/goal-goal-id",
            Path.Combine(root, "worktrees", goal.Id),
            "abc123",
            "Active",
            now);
        StoredPlanSnapshot decided = await store.DecidePlanAsync(
            new("approval-id", goal.Id, plan.Id, "Plan", "Approved", "Proceed.", now),
            worktree,
            "AwaitingPlanApproval",
            "Pending",
            "Approved",
            "Approved");

        Assert.Equal("AwaitingPlanApproval", proposed.Goal.State);
        Assert.Equal("Approved", decided.Goal.State);
        Assert.Equal("Approved", decided.Plan.State);
        Assert.Equal("Proceed.", decided.Approval?.Reason);
        Assert.Equal("Active", decided.Worktree?.State);
        Assert.Equal(worktree.Path, (await store.GetWorktreeAsync(goal.Id))?.Path);
        Assert.Equal("Approved", (await store.GetCurrentPlanAsync(goal.Id))?.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DecidePlanAsync(
            new("second-approval", goal.Id, plan.Id, "Plan", "Approved", null, now),
            null,
            "AwaitingPlanApproval",
            "Pending",
            "Approved",
            "Approved").AsTask());

        using SqliteConnection connection = new($"Data Source={paths.Current.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM approvals WHERE plan_id = 'plan-id';";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
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
