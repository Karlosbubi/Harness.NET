using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Tools;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Approvals;

public sealed class CapabilityApprovalServiceTests
{
    [Fact]
    public async Task Requests_and_approves_restore_for_the_registered_entry_point()
    {
        FakeCapabilityApprovalStore store = new();
        CapabilityApprovalService service = CreateService(store, isTrusted: true);

        CapabilityApprovalResult requested = await service.RequestAsync(new(
            "goal-id",
            new ToolCorrelationId("restore-call"),
            CapabilityKind.Restore,
            "The approved change requires restored packages."));
        CapabilityApprovalResult decided = await service.DecideAsync(new(
            requested.Approval!.Id,
            CapabilityDecision.Approve,
            "Approved for this restore."));

        Assert.Null(requested.Error);
        Assert.Equal("Repository.slnx", requested.Approval.Target);
        Assert.Equal(CapabilityApprovalState.Pending, requested.Approval.State);
        Assert.Null(decided.Error);
        Assert.Equal(CapabilityApprovalState.Approved, decided.Approval?.State);
        Assert.Equal("restore-call", decided.Approval?.CorrelationId.Value);
    }

    [Fact]
    public async Task Denial_requires_a_reason()
    {
        FakeCapabilityApprovalStore store = new();
        CapabilityApprovalService service = CreateService(store, isTrusted: true);
        CapabilityApprovalResult requested = await service.RequestAsync(new(
            "goal-id",
            new ToolCorrelationId("restore-call"),
            CapabilityKind.Restore,
            "Restore dependencies."));

        CapabilityApprovalResult result = await service.DecideAsync(new(
            requested.Approval!.Id,
            CapabilityDecision.Deny,
            Reason: null));

        Assert.Equal("invalid_decision", result.ErrorCode);
        Assert.Equal(
            Harness.DataAccess.Approvals.CapabilityApprovalState.Pending,
            store.Items[0].State);
    }

    [Fact]
    public async Task Untrusted_workspace_cannot_request_network_capability()
    {
        FakeCapabilityApprovalStore store = new();
        CapabilityApprovalService service = CreateService(store, isTrusted: false);

        CapabilityApprovalResult result = await service.RequestAsync(new(
            "goal-id",
            new ToolCorrelationId("restore-call"),
            CapabilityKind.Restore,
            "Restore dependencies."));

        Assert.Equal("goal_not_approved", result.ErrorCode);
        Assert.Empty(store.Items);
    }

    private static CapabilityApprovalService CreateService(
        FakeCapabilityApprovalStore store,
        bool isTrusted) => new(
        new FakeGoalStore(Goal(), Worktree()),
        new FakeWorkspaceStore(Workspace(isTrusted)),
        store);

    private static StoredGoal Goal() => new(
        "goal-id",
        "workspace-id",
        "Goal",
        "Objective",
        3,
        null,
        "Approved",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static StoredGoalWorktree Worktree() => new(
        "goal-id",
        "workspace-id",
        "harness/goal-test",
        "/state/worktrees/goal-id",
        "abc123",
        "Active",
        DateTimeOffset.UtcNow);

    private static RegisteredWorkspace Workspace(bool isTrusted) => new(
        "workspace-id",
        "/workspace/repository",
        "repository",
        "/workspace/repository/Repository.slnx",
        isTrusted,
        IsActive: true,
        "main",
        IsDirty: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class FakeCapabilityApprovalStore
        : Harness.DataAccess.Approvals.ICapabilityApprovalStore
    {
        internal List<Harness.DataAccess.Approvals.StoredCapabilityApproval> Items { get; } = [];

        public ValueTask<Harness.DataAccess.Approvals.StoredCapabilityApprovalStart> StartAsync(
            Harness.DataAccess.Approvals.StoredCapabilityApproval approval,
            CancellationToken cancellationToken = default)
        {
            Harness.DataAccess.Approvals.StoredCapabilityApproval? existing = Items
                .SingleOrDefault(item =>
                    item.GoalId == approval.GoalId &&
                    item.CorrelationId == approval.CorrelationId &&
                    item.Capability == approval.Capability);
            if (existing is not null)
            {
                return ValueTask.FromResult(new Harness.DataAccess.Approvals.StoredCapabilityApprovalStart(
                    existing,
                    WasCreated: false));
            }

            Items.Add(approval);
            return ValueTask.FromResult(new Harness.DataAccess.Approvals.StoredCapabilityApprovalStart(
                approval,
                WasCreated: true));
        }

        public ValueTask<Harness.DataAccess.Approvals.StoredCapabilityApproval> DecideAsync(
            Harness.DataAccess.Approvals.CapabilityApprovalId approvalId,
            Harness.DataAccess.Approvals.CapabilityApprovalState expectedState,
            Harness.DataAccess.Approvals.CapabilityApprovalState nextState,
            string? decisionReason,
            DateTimeOffset decidedAt,
            CancellationToken cancellationToken = default)
        {
            int index = Items.FindIndex(item =>
                item.Id == approvalId && item.State == expectedState);
            if (index < 0)
            {
                throw new InvalidOperationException();
            }

            Items[index] = Items[index] with
            {
                State = nextState,
                DecisionReason = decisionReason,
                DecidedAt = decidedAt,
            };
            return ValueTask.FromResult(Items[index]);
        }

        public ValueTask<Harness.DataAccess.Approvals.StoredCapabilityApproval?> GetByIdAsync(
            Harness.DataAccess.Approvals.CapabilityApprovalId approvalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.SingleOrDefault(item => item.Id == approvalId));

        public ValueTask<Harness.DataAccess.Approvals.StoredCapabilityApproval?> GetAsync(
            string goalId,
            Harness.DataAccess.Tools.ToolCorrelationId correlationId,
            Harness.DataAccess.Approvals.CapabilityKind capability,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.SingleOrDefault(item =>
                item.GoalId == goalId &&
                item.CorrelationId == correlationId &&
                item.Capability == capability));

        public ValueTask<IReadOnlyList<Harness.DataAccess.Approvals.StoredCapabilityApproval>> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Harness.DataAccess.Approvals.StoredCapabilityApproval>>(
                Items.Where(item => item.GoalId == goalId).ToArray());
    }

    private sealed class FakeGoalStore(
        StoredGoal? goal,
        StoredGoalWorktree? worktree) : IGoalStore
    {
        public ValueTask<StoredGoal?> GetAsync(string goalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(goal?.Id == goalId ? goal : null);
        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(string goalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(worktree?.GoalId == goalId ? worktree : null);
        public ValueTask<StoredGoal> CreateAsync(StoredGoal value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredGoal?> UpdateDraftSettingsAsync(string goalId, DateTimeOffset expectedUpdatedAt, int reviewCycleLimit, long? remoteBudgetMicrousd, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(string extensionId, string goalId, long? expectedBudgetMicrousd, long newBudgetMicrousd, string reason, DateTimeOffset approvedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlan?> GetCurrentPlanAsync(string goalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> SavePlanAsync(StoredPlan plan, string expectedGoalState, string nextGoalState, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(StoredApproval approval, StoredGoalWorktree? value, string expectedGoalState, string expectedPlanState, string nextGoalState, string nextPlanState, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeWorkspaceStore(RegisteredWorkspace? workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(workspace);
        public ValueTask<RegisteredWorkspace> SaveAsync(WorkspaceInspection inspection, string entryPoint, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(string rootPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(string workspaceId, bool isTrusted, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
