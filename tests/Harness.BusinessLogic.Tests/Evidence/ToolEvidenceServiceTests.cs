using Harness.BusinessLogic.Evidence;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Evidence;

public sealed class ToolEvidenceServiceTests
{
    [Fact]
    public async Task Lists_expandable_evidence_for_a_goal_in_the_active_workspace()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StoredGoal goal = Goal("workspace-id");
        StoredToolCall evidence = new(
            new Harness.DataAccess.Evidence.ToolCallId("call-id"),
            goal.Id,
            new Harness.DataAccess.Tools.ToolCorrelationId("correlation-id"),
            Harness.DataAccess.Evidence.ToolKind.Build,
            "{\"operation\":\"Build\"}",
            ToolCallState.Succeeded,
            "{\"exitCode\":0}",
            now,
            now);
        ToolEvidenceService service = new(
            new FakeGoalStore(goal),
            new FakeWorkspaceStore(Workspace("workspace-id")),
            new FakeEvidenceStore([evidence]));

        ToolEvidenceSnapshot result = await service.ListAsync(goal.Id);

        ToolEvidenceView item = Assert.Single(result.Items);
        Assert.Null(result.Error);
        Assert.Equal("correlation-id", item.CorrelationId.Value);
        Assert.Equal("{\"exitCode\":0}", item.ResultJson);
    }

    [Fact]
    public async Task Does_not_expose_evidence_when_the_goal_workspace_is_not_active()
    {
        ToolEvidenceService service = new(
            new FakeGoalStore(Goal("other-workspace")),
            new FakeWorkspaceStore(Workspace("workspace-id")),
            new FakeEvidenceStore([]));

        ToolEvidenceSnapshot result = await service.ListAsync("goal-id");

        Assert.Equal("workspace_not_active", result.ErrorCode);
        Assert.Empty(result.Items);
    }

    private static StoredGoal Goal(string workspaceId) => new(
        "goal-id",
        workspaceId,
        "Goal",
        "Objective",
        3,
        null,
        "Approved",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static RegisteredWorkspace Workspace(string workspaceId) => new(
        workspaceId,
        "/workspace",
        "workspace",
        "/workspace/Repository.slnx",
        IsTrusted: true,
        IsActive: true,
        "main",
        IsDirty: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class FakeEvidenceStore(IReadOnlyList<StoredToolCall> items)
        : IToolEvidenceStore
    {
        public ValueTask<IReadOnlyList<StoredToolCall>> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredToolCall>>(
                items.Where(item => item.GoalId == goalId).ToArray());

        public ValueTask<StoredToolCallStart> StartAsync(StoredToolCall toolCall, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredToolCall> CompleteAsync(Harness.DataAccess.Evidence.ToolCallId toolCallId, ToolCallState expectedState, ToolCallState nextState, string resultJson, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeGoalStore(StoredGoal? goal) : IGoalStore
    {
        public ValueTask<StoredGoal?> GetAsync(string goalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(goal?.Id == goalId ? goal : null);
        public ValueTask<StoredGoal> CreateAsync(StoredGoal value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredPlan?> GetCurrentPlanAsync(string goalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> SavePlanAsync(StoredPlan plan, string expectedGoalState, string nextGoalState, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(StoredApproval approval, StoredGoalWorktree? worktree, string expectedGoalState, string expectedPlanState, string nextGoalState, string nextPlanState, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(string goalId, CancellationToken cancellationToken = default) =>
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
