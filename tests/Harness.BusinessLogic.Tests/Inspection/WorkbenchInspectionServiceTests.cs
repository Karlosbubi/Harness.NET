using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Inspection;

public sealed class WorkbenchInspectionServiceTests
{
    [Fact]
    public async Task Search_and_git_share_the_approved_goal_worktree_context()
    {
        TextSearcher searcher = new();
        FileCatalogReader files = new();
        GitInspector git = new();
        DotNetInspector dotNet = new();
        WorkbenchInspectionService service = CreateService(
            CreateGoal("Approved"),
            CreateWorktree(),
            CreateWorkspace(isTrusted: true),
            files,
            searcher,
            git,
            dotNet);
        WorkbenchWorkspaceRequest request = new(new("workspace-id"), new("goal-id"));

        WorkbenchFileCatalogResult catalog = await service.ListFilesAsync(request);
        WorkbenchTextSearchResult search = await service.SearchTextAsync(request, "needle");
        WorkbenchGitInspectionResult inspection = await service.InspectGitAsync(request);
        WorkbenchDotNetInspectionResult solution = await service.InspectDotNetAsync(request);

        Assert.Equal("/state/worktrees/goal-id", files.Root);
        Assert.Equal("/state/worktrees/goal-id", searcher.Root);
        Assert.Equal(searcher.Root, git.Root);
        Assert.Equal(searcher.Root, dotNet.Root);
        Assert.Equal("Harness.slnx", dotNet.EntryPoint);
        Assert.Equal(WorkbenchWorkspaceScope.ApprovedGoalWorktree, search.Context.Scope);
        Assert.Equal(search.Context, inspection.Context);
        Assert.Equal(search.Context, catalog.Context);
        Assert.Equal(search.Context, solution.Context);
        Assert.Equal("src/App/App.csproj", Assert.Single(solution.DotNet.Projects).Path);
        Assert.Equal("harness/goal-test", inspection.Context.Branch?.Value);
        Assert.Equal("src/App.cs", Assert.Single(search.Search.Matches).Path);
        Assert.Equal("src/App.cs", Assert.Single(inspection.Git.Changes).Path);
    }

    [Fact]
    public async Task Unapproved_goal_falls_back_to_an_honest_original_workspace_context()
    {
        TextSearcher searcher = new();
        FileCatalogReader files = new();
        GitInspector git = new();
        DotNetInspector dotNet = new();
        WorkbenchInspectionService service = CreateService(
            CreateGoal("AwaitingPlanApproval"),
            worktree: null,
            CreateWorkspace(isTrusted: true),
            files,
            searcher,
            git,
            dotNet);

        WorkbenchGitInspectionResult result = await service.InspectGitAsync(
            new(new("workspace-id"), new("goal-id")));

        Assert.Equal("/workspace/repository", git.Root);
        Assert.Equal(WorkbenchWorkspaceScope.OriginalWorkspace, result.Context.Scope);
        Assert.Null(result.Context.GoalId);
        Assert.Contains("no active approved worktree", result.Context.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoked_trust_rejects_inspection_before_data_access()
    {
        TextSearcher searcher = new();
        FileCatalogReader files = new();
        GitInspector git = new();
        DotNetInspector dotNet = new();
        WorkbenchInspectionService service = CreateService(
            CreateGoal("Approved"),
            CreateWorktree(),
            CreateWorkspace(isTrusted: false),
            files,
            searcher,
            git,
            dotNet);

        WorkbenchFileCatalogResult catalog = await service.ListFilesAsync(
            new(new("workspace-id"), new("goal-id")));
        WorkbenchTextSearchResult search = await service.SearchTextAsync(
            new(new("workspace-id"), new("goal-id")),
            "needle");
        WorkbenchGitInspectionResult inspection = await service.InspectGitAsync(
            new(new("workspace-id"), new("goal-id")));
        WorkbenchDotNetInspectionResult solution = await service.InspectDotNetAsync(
            new(new("workspace-id"), new("goal-id")));

        Assert.Equal("workspace_not_trusted", catalog.Catalog.ErrorCode);
        Assert.Equal("workspace_not_trusted", search.Search.ErrorCode);
        Assert.Equal("workspace_not_trusted", inspection.Git.ErrorCode);
        Assert.Equal("workspace_not_trusted", solution.DotNet.ErrorCode);
        Assert.Null(files.Root);
        Assert.Null(searcher.Root);
        Assert.Null(git.Root);
        Assert.Null(dotNet.Root);
    }

    private static WorkbenchInspectionService CreateService(
        StoredGoal goal,
        StoredGoalWorktree? worktree,
        RegisteredWorkspace workspace,
        FileCatalogReader files,
        TextSearcher searcher,
        GitInspector git,
        DotNetInspector dotNet) => new(
        new WorkbenchWorkspaceContextResolver(
            new GoalStore(goal, worktree),
            new WorkspaceStore(workspace)),
        files,
        searcher,
        git,
        dotNet);

    private sealed class FileCatalogReader : IWorkspaceFileCatalogReader
    {
        internal string? Root { get; private set; }

        public ValueTask<WorkspaceFileCatalog> ReadAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            Root = workspaceRoot;
            return ValueTask.FromResult(new WorkspaceFileCatalog(
                [new("src/App.cs")],
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }
    }

    private static StoredGoal CreateGoal(string state) => new(
        "goal-id", "workspace-id", "Safe edit", "Edit source", 2, null, state,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static StoredGoalWorktree CreateWorktree() => new(
        "goal-id", "workspace-id", "harness/goal-test", "/state/worktrees/goal-id",
        "abc123", "Active", DateTimeOffset.UtcNow);

    private static RegisteredWorkspace CreateWorkspace(bool isTrusted) => new(
        "workspace-id", "/workspace/repository", "repository",
        "/workspace/repository/Harness.slnx", isTrusted, true, "main", false,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class TextSearcher : IWorkspaceTextSearcher
    {
        internal string? Root { get; private set; }

        public ValueTask<WorkspaceTextSearch> SearchAsync(
            string workspaceRoot,
            string query,
            CancellationToken cancellationToken = default)
        {
            Root = workspaceRoot;
            return ValueTask.FromResult(new WorkspaceTextSearch(
                [new("src/App.cs", 1, query)], 1, false, null, null));
        }
    }

    private sealed class GitInspector : IWorkspaceGitInspector
    {
        internal string? Root { get; private set; }

        public ValueTask<WorkspaceGitState> InspectAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            Root = workspaceRoot;
            return ValueTask.FromResult(new WorkspaceGitState(
                "harness/goal-test", "abc123", [new("src/App.cs", "modified")],
                "diff --git a/src/App.cs b/src/App.cs", false, null, null));
        }
    }

    private sealed class DotNetInspector : IWorkspaceDotNetInspector
    {
        internal string? Root { get; private set; }
        internal string? EntryPoint { get; private set; }

        public ValueTask<WorkspaceDotNetInfo> InspectAsync(
            string workspaceRoot,
            string entryPoint,
            CancellationToken cancellationToken = default)
        {
            Root = workspaceRoot;
            EntryPoint = entryPoint;
            return ValueTask.FromResult(new WorkspaceDotNetInfo(
                entryPoint,
                "slnx",
                new("10.0.100", "latestFeature", false),
                [new("src/App/App.csproj", "Microsoft.NET.Sdk", ["net10.0"],
                    "latest", "enable", [new("package", "Example", "1.0.0")])],
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }
    }

    private sealed class GoalStore(StoredGoal goal, StoredGoalWorktree? worktree) : IGoalStore
    {
        public ValueTask<StoredGoal?> GetAsync(string goalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StoredGoal?>(goalId == goal.Id ? goal : null);
        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(string goalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(goalId == goal.Id ? worktree : null);
        public ValueTask<StoredGoal> CreateAsync(StoredGoal value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoal?> UpdateDraftSettingsAsync(string goalId, DateTimeOffset expectedUpdatedAt, int reviewCycleLimit, long? remoteBudgetMicrousd, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(string extensionId, string goalId, long? expectedBudgetMicrousd, long newBudgetMicrousd, string reason, DateTimeOffset approvedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlan?> GetCurrentPlanAsync(string goalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> SavePlanAsync(StoredPlan plan, string expectedGoalState, string nextGoalState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(StoredApproval approval, StoredGoalWorktree? value, string expectedGoalState, string expectedPlanState, string nextGoalState, string nextPlanState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class WorkspaceStore(RegisteredWorkspace workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(workspace);
        public ValueTask<RegisteredWorkspace> SaveAsync(WorkspaceInspection inspection, string entryPoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(string workspaceId, bool isTrusted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
