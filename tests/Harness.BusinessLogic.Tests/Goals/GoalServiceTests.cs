using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Tests.Goals;

public sealed class GoalServiceTests
{
    [Fact]
    public async Task Creates_a_draft_goal_for_the_active_workspace()
    {
        FakeGoalStore store = new();
        GoalService service = new(store, new FakeWorkspaceStore(CreateWorkspace()));

        GoalResult result = await service.CreateAsync(new(
            "workspace-id",
            " Add typed edits ",
            " Implement exact replacement edits. ",
            3,
            1_000_000));

        Assert.Null(result.Error);
        Assert.Equal("Draft", result.Goal?.State);
        Assert.Equal("Add typed edits", result.Goal?.Title);
        Assert.Equal("Implement exact replacement edits.", result.Goal?.Objective);
        Assert.Equal("workspace-id", store.Created?.WorkspaceId);
    }

    [Fact]
    public async Task Rejects_invalid_caps_without_persisting()
    {
        FakeGoalStore store = new();
        GoalService service = new(store, new FakeWorkspaceStore(CreateWorkspace()));

        GoalResult result = await service.CreateAsync(new(
            "workspace-id",
            "Goal",
            "Objective",
            0,
            -1));

        Assert.Equal("invalid_goal", result.ErrorCode);
        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Rejects_a_goal_for_a_non_active_workspace()
    {
        FakeGoalStore store = new();
        GoalService service = new(store, new FakeWorkspaceStore(CreateWorkspace()));

        GoalResult result = await service.CreateAsync(new(
            "another-workspace",
            "Goal",
            "Objective",
            2,
            null));

        Assert.Equal("workspace_not_active", result.ErrorCode);
        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Proposes_a_versioned_plan_and_waits_for_approval()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = new(store, new FakeWorkspaceStore(CreateWorkspace()));

        PlanResult result = await service.ProposePlanAsync(new(goal.Id, "1. Implement\n2. Test"));

        Assert.Null(result.Error);
        Assert.Equal("AwaitingPlanApproval", result.Goal?.State);
        Assert.Equal("Pending", result.Plan?.State);
        Assert.Equal(1, result.Plan?.Revision);
    }

    [Fact]
    public async Task Approval_requires_a_trusted_workspace()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = new(store, new FakeWorkspaceStore(CreateWorkspace()));
        PlanResult proposal = await service.ProposePlanAsync(new(goal.Id, "Implement and test."));

        PlanResult result = await service.DecidePlanAsync(new(
            goal.Id,
            proposal.Plan!.Id,
            "Approve",
            Reason: null));

        Assert.Equal("workspace_not_trusted", result.ErrorCode);
        Assert.Equal("Pending", store.CurrentPlan?.State);
    }

    [Fact]
    public async Task Denial_persists_reason_and_allows_a_new_revision()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = new(store, new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)));
        PlanResult first = await service.ProposePlanAsync(new(goal.Id, "First plan"));

        PlanResult denied = await service.DecidePlanAsync(new(
            goal.Id,
            first.Plan!.Id,
            "Deny",
            "Add migration tests."));
        PlanResult revised = await service.ProposePlanAsync(new(goal.Id, "Revised plan with migration tests"));

        Assert.Equal("Denied", denied.Approval?.Decision);
        Assert.Equal("Add migration tests.", denied.Approval?.Reason);
        Assert.Equal(2, revised.Plan?.Revision);
        Assert.Equal("Pending", revised.Plan?.State);
    }

    [Fact]
    public async Task Trusted_workspace_can_approve_the_current_plan_once()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = new(store, new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)));
        PlanResult proposal = await service.ProposePlanAsync(new(goal.Id, "Implement and test."));

        PlanResult approved = await service.DecidePlanAsync(new(
            goal.Id,
            proposal.Plan!.Id,
            "Approve",
            "Proceed."));
        PlanResult duplicate = await service.DecidePlanAsync(new(
            goal.Id,
            proposal.Plan.Id,
            "Approve",
            null));

        Assert.Equal("Approved", approved.Goal?.State);
        Assert.Equal("Approved", approved.Plan?.State);
        Assert.Equal("Approved", approved.Approval?.Decision);
        Assert.Equal("invalid_transition", duplicate.ErrorCode);
    }

    private static RegisteredWorkspace CreateWorkspace(bool isTrusted = false) => new(
        "workspace-id",
        "/workspace/repository",
        "repository",
        "/workspace/repository/Repository.slnx",
        IsTrusted: isTrusted,
        IsActive: true,
        "main",
        IsDirty: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static StoredGoal CreateGoal(string state) => new(
        "goal-id",
        "workspace-id",
        "Goal",
        "Objective",
        3,
        null,
        state,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class FakeGoalStore(StoredGoal? initial = null) : IGoalStore
    {
        internal StoredGoal? Created { get; private set; } = initial;
        internal StoredPlan? CurrentPlan { get; private set; }

        public ValueTask<StoredGoal> CreateAsync(
            StoredGoal goal,
            CancellationToken cancellationToken = default)
        {
            Created = goal;
            return ValueTask.FromResult(goal);
        }

        public ValueTask<StoredGoal?> GetAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Created?.Id == goalId ? Created : null);

        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredGoal>>(
                Created?.WorkspaceId == workspaceId ? [Created] : []);

        public ValueTask<StoredPlan?> GetCurrentPlanAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CurrentPlan?.GoalId == goalId ? CurrentPlan : null);

        public ValueTask<StoredPlanSnapshot> SavePlanAsync(
            StoredPlan plan,
            string expectedGoalState,
            string nextGoalState,
            CancellationToken cancellationToken = default)
        {
            if (Created?.State != expectedGoalState)
            {
                throw new InvalidOperationException("State changed.");
            }

            Created = Created with { State = nextGoalState, UpdatedAt = plan.UpdatedAt };
            CurrentPlan = plan;
            return ValueTask.FromResult(new StoredPlanSnapshot(Created, plan, null));
        }

        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(
            StoredApproval approval,
            string expectedGoalState,
            string expectedPlanState,
            string nextGoalState,
            string nextPlanState,
            CancellationToken cancellationToken = default)
        {
            if (Created?.State != expectedGoalState || CurrentPlan?.State != expectedPlanState)
            {
                throw new InvalidOperationException("State changed.");
            }

            Created = Created with { State = nextGoalState, UpdatedAt = approval.DecidedAt };
            CurrentPlan = CurrentPlan with
            {
                State = nextPlanState,
                UpdatedAt = approval.DecidedAt,
            };
            return ValueTask.FromResult(new StoredPlanSnapshot(Created, CurrentPlan, approval));
        }
    }

    private sealed class FakeWorkspaceStore(RegisteredWorkspace? workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(workspace);

        public ValueTask<RegisteredWorkspace> SaveAsync(
            WorkspaceInspection inspection,
            string entryPoint,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace?> FindByPathAsync(
            string rootPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetActiveAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetTrustAsync(
            string workspaceId,
            bool isTrusted,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
