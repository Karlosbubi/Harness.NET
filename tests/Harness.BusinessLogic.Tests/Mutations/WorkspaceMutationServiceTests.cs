using Harness.BusinessLogic.Mutations;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Mutations;

public sealed class WorkspaceMutationServiceTests
{
    [Fact]
    public async Task Approved_goal_uses_its_persisted_worktree_and_preserves_correlation()
    {
        FakeFileEditor editor = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            "tool-call-42",
            "Program.cs",
            "expected",
            "replacement"));

        Assert.Null(result.Error);
        Assert.Equal("tool-call-42", result.CorrelationId);
        Assert.Equal("/state/worktrees/goal-id", editor.Root);
        Assert.Equal(1, editor.CallCount);
    }

    [Fact]
    public async Task Goal_without_an_active_grant_cannot_edit()
    {
        FakeFileEditor editor = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Draft"), worktree: null),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            "tool-call-43",
            "Program.cs",
            null,
            "replacement"));

        Assert.Equal("goal_not_approved", result.ErrorCode);
        Assert.Equal(0, editor.CallCount);
    }

    [Fact]
    public async Task Revoked_workspace_trust_blocks_an_approved_goal()
    {
        FakeFileEditor editor = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: false)),
            editor);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            "tool-call-44",
            "Program.cs",
            null,
            "replacement"));

        Assert.Equal("workspace_not_trusted", result.ErrorCode);
        Assert.Equal(0, editor.CallCount);
    }

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

    private static StoredGoalWorktree CreateWorktree() => new(
        "goal-id",
        "workspace-id",
        "harness/goal-test",
        "/state/worktrees/goal-id",
        "abc123",
        "Active",
        DateTimeOffset.UtcNow);

    private static RegisteredWorkspace CreateWorkspace(bool isTrusted) => new(
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

    private sealed class FakeFileEditor : IWorkspaceFileEditor
    {
        internal int CallCount { get; private set; }
        internal string? Root { get; private set; }

        public ValueTask<WorkspaceFileEditResult> ApplyAsync(
            string worktreeRoot,
            WorkspaceFileEdit edit,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Root = worktreeRoot;
            return ValueTask.FromResult(new WorkspaceFileEditResult(
                edit.Path,
                edit.ExpectedSha256,
                "new-hash",
                11,
                WasCreated: false,
                ErrorCode: null,
                Error: null));
        }
    }

    private sealed class FakeGoalStore(
        StoredGoal? goal,
        StoredGoalWorktree? worktree) : IGoalStore
    {
        public ValueTask<StoredGoal?> GetAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(goal?.Id == goalId ? goal : null);

        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(worktree?.GoalId == goalId ? worktree : null);

        public ValueTask<StoredGoal> CreateAsync(StoredGoal value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
