using Harness.DataAccess.Configuration;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using Harness.DataAccess.Workflows;
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
    public async Task Updates_only_an_exact_draft_settings_snapshot()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        string workspaceRoot = Path.Combine(root, "repository");
        string entryPoint = Path.Combine(workspaceRoot, "Repository.slnx");
        RegisteredWorkspace workspace = await new SqliteWorkspaceStore(paths).SaveAsync(
            new(workspaceRoot, "repository", "main", false, [entryPoint], Error: null),
            entryPoint);
        SqliteGoalStore store = new(paths);
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-07-29T10:00:00Z");
        StoredGoal goal = await store.CreateAsync(new(
            "goal-id", workspace.Id, "Goal", "Objective", 3, null, "Draft",
            createdAt, createdAt));
        DateTimeOffset updatedAt = createdAt.AddMinutes(1);

        StoredGoal? updated = await store.UpdateDraftSettingsAsync(
            goal.Id, goal.UpdatedAt, 5, 2_000_000, updatedAt);
        StoredGoal? stale = await store.UpdateDraftSettingsAsync(
            goal.Id, goal.UpdatedAt, 6, null, updatedAt.AddMinutes(1));

        Assert.Equal(5, updated?.ReviewCycleLimit);
        Assert.Equal(2_000_000, updated?.RemoteBudgetMicrousd);
        Assert.Equal(updatedAt, updated?.UpdatedAt);
        Assert.Null(stale);
    }

    [Fact]
    public async Task Budget_extension_is_increase_only_cas_and_audited_atomically()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        string workspaceRoot = Path.Combine(root, "repository");
        string entryPoint = Path.Combine(workspaceRoot, "Repository.slnx");
        RegisteredWorkspace workspace = await new SqliteWorkspaceStore(paths).SaveAsync(
            new(workspaceRoot, "repository", "main", false, [entryPoint], Error: null),
            entryPoint);
        SqliteGoalStore store = new(paths);
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-31T13:00:00Z");
        StoredGoal goal = await store.CreateAsync(new(
            "goal-id", workspace.Id, "Goal", "Objective", 3, 1_000_000, "Approved",
            now, now));

        StoredGoalBudgetExtensionSnapshot? extended = await store.ExtendRemoteBudgetAsync(
            "extension-id", goal.Id, 1_000_000, 2_000_000, "Explicit retry budget.",
            now.AddMinutes(1));
        StoredGoalBudgetExtensionSnapshot? stale = await store.ExtendRemoteBudgetAsync(
            "stale-id", goal.Id, 1_000_000, 3_000_000, "Stale.", now.AddMinutes(2));
        StoredGoalBudgetExtensionSnapshot? decrease = await store.ExtendRemoteBudgetAsync(
            "decrease-id", goal.Id, 2_000_000, 1_500_000, "Decrease.", now.AddMinutes(2));

        Assert.Equal(2_000_000, extended?.Goal.RemoteBudgetMicrousd);
        Assert.Equal(1_000_000, extended?.Extension.PreviousBudgetMicrousd);
        Assert.Equal("Explicit retry budget.", extended?.Extension.Reason);
        Assert.Null(stale);
        Assert.Null(decrease);
        using SqliteConnection connection = new($"Data Source={paths.Current.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM goal_budget_extensions WHERE goal_id = 'goal-id';";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
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

    [Fact]
    public async Task Aborted_goals_are_hidden_from_resumable_list_but_remain_auditable()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        string workspaceRoot = Path.Combine(root, "repository");
        string entryPoint = Path.Combine(workspaceRoot, "Repository.slnx");
        RegisteredWorkspace workspace = await new SqliteWorkspaceStore(paths).SaveAsync(
            new(workspaceRoot, "repository", "main", false, [entryPoint], Error: null),
            entryPoint);
        SqliteGoalStore goalStore = new(paths);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
        string goalId = Guid.NewGuid().ToString("N");
        StoredGoal goal = await goalStore.CreateAsync(new(
            goalId, workspace.Id, "Goal", "Objective", 3, null, "Draft", now, now));

        await new SqliteGoalWorkflowStore(paths).AbortAsync(
            new(goal.Id), new("Stopped by user."), now.AddMinutes(1));

        Assert.Empty(await goalStore.ListAsync(workspace.Id));
        Assert.Equal(goal.Id, (await goalStore.GetAsync(goal.Id))?.Id);
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
