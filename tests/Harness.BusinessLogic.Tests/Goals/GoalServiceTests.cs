using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Costs;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Goals;

public sealed class GoalServiceTests
{
    [Fact]
    public async Task Creates_a_draft_goal_for_the_active_workspace()
    {
        FakeGoalStore store = new();
        GoalService service = CreateService(store, CreateWorkspace());

        GoalResult result = await service.CreateAsync(new(
            "workspace-id",
            " Add typed edits ",
            " Implement exact replacement edits. ",
            new(3),
            new(1_000_000)));

        Assert.Null(result.Error);
        Assert.Equal(GoalState.Draft, result.Goal?.State);
        Assert.Equal("Add typed edits", result.Goal?.Title);
        Assert.Equal("Implement exact replacement edits.", result.Goal?.Objective);
        Assert.Equal("workspace-id", store.Created?.WorkspaceId);
    }

    [Fact]
    public async Task Rejects_invalid_caps_without_persisting()
    {
        FakeGoalStore store = new();
        GoalService service = CreateService(store, CreateWorkspace());

        GoalResult result = await service.CreateAsync(new(
            "workspace-id",
            "Goal",
            "Objective",
            new(0),
            new(-1)));

        Assert.Equal("invalid_goal", result.ErrorCode);
        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Rejects_a_goal_for_a_non_active_workspace()
    {
        FakeGoalStore store = new();
        GoalService service = CreateService(store, CreateWorkspace());

        GoalResult result = await service.CreateAsync(new(
            "another-workspace",
            "Goal",
            "Objective",
            new(2),
            null));

        Assert.Equal("workspace_not_active", result.ErrorCode);
        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Proposes_a_versioned_plan_and_waits_for_approval()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = CreateService(store, CreateWorkspace());

        PlanResult result = await service.ProposePlanAsync(new(new(goal.Id), "1. Implement\n2. Test"));

        Assert.Null(result.Error);
        Assert.Equal(GoalState.AwaitingPlanApproval, result.Goal?.State);
        Assert.Equal(PlanState.Pending, result.Plan?.State);
        Assert.Equal(1, result.Plan?.Revision.Value);
    }

    [Fact]
    public async Task Approval_requires_a_trusted_workspace()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = CreateService(store, CreateWorkspace());
        PlanResult proposal = await service.ProposePlanAsync(new(new(goal.Id), "Implement and test."));

        PlanResult result = await service.DecidePlanAsync(new(
            new(goal.Id),
            proposal.Plan!.Id,
            PlanDecision.Approve,
            Reason: null));

        Assert.Equal("workspace_not_trusted", result.ErrorCode);
        Assert.Equal("Pending", store.CurrentPlan?.State);
    }

    [Fact]
    public async Task Rejects_an_undefined_plan_decision()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = CreateService(store, CreateWorkspace(isTrusted: true));
        PlanResult proposal = await service.ProposePlanAsync(new(new(goal.Id), "Implement and test."));

        PlanResult result = await service.DecidePlanAsync(new(
            new(goal.Id),
            proposal.Plan!.Id,
            (PlanDecision)int.MaxValue,
            null));

        Assert.Equal("invalid_decision", result.ErrorCode);
        Assert.Equal("Pending", store.CurrentPlan?.State);
    }

    [Fact]
    public async Task Denial_persists_reason_and_allows_a_new_revision()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = CreateService(store, CreateWorkspace(isTrusted: true));
        PlanResult first = await service.ProposePlanAsync(new(new(goal.Id), "First plan"));

        PlanResult denied = await service.DecidePlanAsync(new(
            new(goal.Id),
            first.Plan!.Id,
            PlanDecision.Deny,
            "Add migration tests."));
        PlanResult revised = await service.ProposePlanAsync(new(new(goal.Id), "Revised plan with migration tests"));

        Assert.Equal(ApprovalDecision.Denied, denied.Approval?.Decision);
        Assert.Equal("Add migration tests.", denied.Approval?.Reason);
        Assert.Equal(2, revised.Plan?.Revision.Value);
        Assert.Equal(PlanState.Pending, revised.Plan?.State);
    }

    [Fact]
    public async Task Trusted_workspace_can_approve_the_current_plan_once()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        GoalService service = CreateService(store, CreateWorkspace(isTrusted: true));
        PlanResult proposal = await service.ProposePlanAsync(new(new(goal.Id), "Implement and test."));

        PlanResult approved = await service.DecidePlanAsync(new(
            new(goal.Id),
            proposal.Plan!.Id,
            PlanDecision.Approve,
            "Proceed."));
        PlanResult duplicate = await service.DecidePlanAsync(new(
            new(goal.Id),
            proposal.Plan.Id,
            PlanDecision.Approve,
            null));

        Assert.Equal(GoalState.Approved, approved.Goal?.State);
        Assert.Equal(PlanState.Approved, approved.Plan?.State);
        Assert.Equal(ApprovalDecision.Approved, approved.Approval?.Decision);
        Assert.Equal(GoalWorktreeState.Active, approved.Worktree?.State);
        Assert.Equal("/state/worktrees/goal-id", approved.Worktree?.Path);
        Assert.Equal("invalid_transition", duplicate.ErrorCode);
    }

    [Fact]
    public async Task Worktree_failure_leaves_the_plan_pending_without_a_grant()
    {
        StoredGoal goal = CreateGoal("Draft");
        FakeGoalStore store = new(goal);
        FakeGoalWorktreeManager manager = new(new(
            goal.Id,
            string.Empty,
            string.Empty,
            string.Empty,
            WasCreated: false,
            "worktree_create_failed",
            "Git failed."));
        GoalService service = CreateService(store, CreateWorkspace(isTrusted: true), manager);
        PlanResult proposal = await service.ProposePlanAsync(new(new(goal.Id), "Implement and test."));

        PlanResult result = await service.DecidePlanAsync(new(
            new(goal.Id),
            proposal.Plan!.Id,
            PlanDecision.Approve,
            null));

        Assert.Equal("worktree_create_failed", result.ErrorCode);
        Assert.Equal("AwaitingPlanApproval", store.Created?.State);
        Assert.Equal("Pending", store.CurrentPlan?.State);
        Assert.Null(store.Worktree);
    }

    private static GoalService CreateService(
        FakeGoalStore store,
        RegisteredWorkspace workspace,
        IGoalWorktreeManager? worktreeManager = null) =>
        new(
            store,
            new FakeWorkspaceStore(workspace),
            worktreeManager ?? new FakeGoalWorktreeManager());

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
            return ValueTask.FromResult(new StoredPlanSnapshot(Created, plan, null, null));
        }

        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(
            StoredApproval approval,
            StoredGoalWorktree? worktree,
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
            Worktree = worktree;
            return ValueTask.FromResult(new StoredPlanSnapshot(Created, CurrentPlan, approval, worktree));
        }

        internal StoredGoalWorktree? Worktree { get; private set; }

        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Worktree?.GoalId == goalId ? Worktree : null);
    }

    private sealed class FakeGoalWorktreeManager(GoalWorktreeResult? result = null)
        : IGoalWorktreeManager
    {
        public ValueTask<GoalWorktreeResult> CreateAsync(
            string goalId,
            string repositoryRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result ?? new GoalWorktreeResult(
                goalId,
                "harness/goal-test",
                $"/state/worktrees/{goalId}",
                "abc123",
                WasCreated: true,
                ErrorCode: null,
                Error: null));
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
