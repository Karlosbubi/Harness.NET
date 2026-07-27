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

    private static RegisteredWorkspace CreateWorkspace() => new(
        "workspace-id",
        "/workspace/repository",
        "repository",
        "/workspace/repository/Repository.slnx",
        IsTrusted: false,
        IsActive: true,
        "main",
        IsDirty: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class FakeGoalStore : IGoalStore
    {
        internal StoredGoal? Created { get; private set; }

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
