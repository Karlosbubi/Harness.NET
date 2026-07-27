using Harness.DataAccess.Approvals;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Tools;
using Harness.DataAccess.Workspaces;

namespace Harness.DataAccess.Tests.Approvals;

public sealed class SqliteCapabilityApprovalStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-capability-approval-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_one_scoped_request_and_decides_it_atomically()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        string goalId = await CreateGoalAsync(paths);
        SqliteCapabilityApprovalStore store = new(paths);
        DateTimeOffset requestedAt = DateTimeOffset.UtcNow;
        StoredCapabilityApproval requested = new(
            new("0123456789abcdef0123456789abcdef"),
            goalId,
            new("restore-call"),
            CapabilityKind.Restore,
            "Repository.slnx",
            "Restore packages required by the approved plan.",
            CapabilityApprovalState.Pending,
            DecisionReason: null,
            requestedAt,
            DecidedAt: null);

        StoredCapabilityApprovalStart first = await store.StartAsync(requested);
        StoredCapabilityApprovalStart duplicate = await store.StartAsync(requested with
        {
            Id = new("fedcba9876543210fedcba9876543210"),
        });
        StoredCapabilityApproval approved = await store.DecideAsync(
            requested.Id,
            CapabilityApprovalState.Pending,
            CapabilityApprovalState.Approved,
            "Approved for this restore only.",
            requestedAt.AddSeconds(1));
        StoredCapabilityApproval? loaded = await store.GetAsync(
            goalId,
            new ToolCorrelationId("restore-call"),
            CapabilityKind.Restore);

        Assert.True(first.WasCreated);
        Assert.False(duplicate.WasCreated);
        Assert.Equal(requested.Id, duplicate.Approval.Id);
        Assert.Equal(CapabilityApprovalState.Approved, approved.State);
        Assert.Equal("Repository.slnx", loaded?.Target);
        Assert.Equal(approved, Assert.Single(await store.ListAsync(goalId)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DecideAsync(
            requested.Id,
            CapabilityApprovalState.Pending,
            CapabilityApprovalState.Denied,
            "Too late.",
            requestedAt.AddSeconds(2)).AsTask());
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
