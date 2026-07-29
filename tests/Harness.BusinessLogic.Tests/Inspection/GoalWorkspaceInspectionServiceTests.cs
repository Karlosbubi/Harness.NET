using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Inspection;

public sealed class GoalWorkspaceInspectionServiceTests
{
    [Fact]
    public async Task Lead_inspection_reads_the_trusted_original_workspace()
    {
        CapturingFileReader reader = new();
        GoalWorkspaceInspectionService service = CreateService(
            CreateGoal("Planning"),
            worktree: null,
            reader);

        WorkspaceFileView result = await service.ReadFileAsync(
            new("goal-1"),
            GoalWorkspaceScope.Original,
            "README.md");

        Assert.Null(result.Error);
        Assert.Equal("/workspace/repository", reader.RootPath);
    }

    [Fact]
    public async Task Implementer_and_reviewer_inspection_requires_the_approved_worktree()
    {
        CapturingFileReader approvedReader = new();
        GoalWorkspaceInspectionService approved = CreateService(
            CreateGoal("Approved"),
            new("goal-1", "workspace-1", "harness/goal-1", "/workspace/worktree",
                "abc123", "Active", DateTimeOffset.UtcNow),
            approvedReader);
        GoalWorkspaceInspectionService planning = CreateService(
            CreateGoal("Planning"),
            worktree: null,
            new());

        WorkspaceFileView allowed = await approved.ReadFileAsync(
            new("goal-1"),
            GoalWorkspaceScope.ApprovedWorktree,
            "src/Program.cs");
        WorkspaceFileView denied = await planning.ReadFileAsync(
            new("goal-1"),
            GoalWorkspaceScope.ApprovedWorktree,
            "src/Program.cs");

        Assert.Null(allowed.Error);
        Assert.Equal("/workspace/worktree", approvedReader.RootPath);
        Assert.Equal("goal_workspace_unavailable", denied.ErrorCode);
    }

    private static GoalWorkspaceInspectionService CreateService(
        StoredGoal goal,
        StoredGoalWorktree? worktree,
        CapturingFileReader reader) => new(
        new StubGoalStore(goal, worktree),
        new StubWorkspaceStore(new(
            "workspace-1",
            "/workspace/repository",
            "repository",
            "/workspace/repository/Harness.slnx",
            IsTrusted: true,
            IsActive: true,
            "main",
            IsDirty: false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)),
        reader,
        new UnsupportedTextSearcher(),
        new UnsupportedGitInspector(),
        new UnsupportedDotNetInspector());

    private static StoredGoal CreateGoal(string state) => new(
        "goal-1",
        "workspace-1",
        "Title",
        "Objective",
        2,
        RemoteBudgetMicrousd: null,
        state,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class CapturingFileReader : IWorkspaceFileReader
    {
        internal string? RootPath { get; private set; }

        public ValueTask<WorkspaceFileRead> ReadAsync(
            string workspaceRoot,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            RootPath = workspaceRoot;
            return ValueTask.FromResult(new WorkspaceFileRead(
                relativePath, "content", 7, false, null, null));
        }
    }

    private sealed class StubGoalStore(
        StoredGoal goal,
        StoredGoalWorktree? worktree) : IGoalStore
    {
        public ValueTask<StoredGoal?> GetAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StoredGoal?>(goalId == goal.Id ? goal : null);

        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(goalId == goal.Id ? worktree : null);

        public ValueTask<StoredGoal> CreateAsync(
            StoredGoal value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredGoal?> UpdateDraftSettingsAsync(
            string goalId, DateTimeOffset expectedUpdatedAt, int reviewCycleLimit,
            long? remoteBudgetMicrousd, DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredPlan?> GetCurrentPlanAsync(
            string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredPlanSnapshot> SavePlanAsync(
            StoredPlan plan,
            string expectedGoalState,
            string nextGoalState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(
            StoredApproval approval,
            StoredGoalWorktree? value,
            string expectedGoalState,
            string expectedPlanState,
            string nextGoalState,
            string nextPlanState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubWorkspaceStore(RegisteredWorkspace workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(workspace);

        public ValueTask<RegisteredWorkspace> SaveAsync(
            WorkspaceInspection inspection,
            string entryPoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace?> FindByPathAsync(
            string rootPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetActiveAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetTrustAsync(
            string workspaceId,
            bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedTextSearcher : IWorkspaceTextSearcher
    {
        public ValueTask<WorkspaceTextSearch> SearchAsync(
            string workspaceRoot,
            string query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedGitInspector : IWorkspaceGitInspector
    {
        public ValueTask<WorkspaceGitState> InspectAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedDotNetInspector : IWorkspaceDotNetInspector
    {
        public ValueTask<WorkspaceDotNetInfo> InspectAsync(
            string workspaceRoot,
            string entryPoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
