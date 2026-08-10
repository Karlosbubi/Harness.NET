using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.CodeIntelligence;

public sealed class GoalCodeIntelligenceServiceTests
{
    private const string Baseline =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Lead_symbol_query_uses_original_context_and_closes_session()
    {
        CapturingCodeIntelligence code = new();
        GoalCodeIntelligenceService service = CreateService(code, GoalWorkspaceScope.Original);

        GoalCodeSymbolView result = await service.GetSymbolAsync(
            new("goal-1"),
            GoalWorkspaceScope.Original,
            new("src/Program.cs"),
            new(2, 4));

        Assert.Equal(WorkbenchCodeResultState.Ready, result.State);
        Assert.Equal("symbol", Assert.Single(result.Sections).Value);
        Assert.Null(code.StartRequest?.GoalId);
        Assert.Equal("src/Program.cs", code.QuickInfoSnapshot?.Path.Value);
        Assert.Equal(Baseline, code.QuickInfoSnapshot?.BaselineHash.Value);
        Assert.Equal(2, code.QuickInfoSnapshot?.Position.Line);
        Assert.True(code.WasStopped);
    }

    [Fact]
    public async Task Worktree_reference_query_is_bound_to_the_goal_context()
    {
        CapturingCodeIntelligence code = new();
        GoalCodeIntelligenceService service = CreateService(
            code, GoalWorkspaceScope.ApprovedWorktree);

        GoalCodeNavigationView result = await service.FindReferencesAsync(
            new("goal-1"),
            GoalWorkspaceScope.ApprovedWorktree,
            new("src/Program.cs"),
            new(1, 3));

        Assert.Equal("goal-1", code.StartRequest?.GoalId?.Value);
        Assert.Equal("src/Reference.cs", Assert.Single(result.Destinations).Path?.Value);
        Assert.True(code.WasStopped);
    }

    [Fact]
    public async Task Truncated_source_is_rejected_before_starting_roslyn()
    {
        CapturingCodeIntelligence code = new();
        GoalCodeIntelligenceService service = CreateService(
            code, GoalWorkspaceScope.Original, isTruncated: true);

        GoalCodeProblemsView result = await service.InspectProblemsAsync(
            new("goal-1"), GoalWorkspaceScope.Original, new("src/Large.cs"));

        Assert.Equal(WorkbenchCodeResultState.Failed, result.State);
        Assert.Equal("source_file_too_large", result.Issue?.Code.Value);
        Assert.Null(code.StartRequest);
        Assert.False(code.WasStopped);
    }

    private static GoalCodeIntelligenceService CreateService(
        CapturingCodeIntelligence code,
        GoalWorkspaceScope expectedScope,
        bool isTruncated = false) => new(
        new StubGoalStore(),
        new StubWorkspaceStore(),
        new StubInspectionService(expectedScope, isTruncated),
        code);

    private sealed class StubInspectionService(
        GoalWorkspaceScope expectedScope,
        bool isTruncated) : IGoalWorkspaceInspectionService
    {
        public ValueTask<WorkspaceFileView> ReadFileAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedScope, scope);
            return ValueTask.FromResult(new WorkspaceFileView(
                relativePath,
                "class Program { static void Main() { } }",
                Baseline,
                40,
                isTruncated,
                null,
                null));
        }

        public ValueTask<WorkspaceTextSearchView> SearchTextAsync(
            GoalId goalId, GoalWorkspaceScope scope, string query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkspaceGitStateView> InspectGitAsync(
            GoalId goalId, GoalWorkspaceScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkspaceDotNetInfoView> InspectDotNetAsync(
            GoalId goalId, GoalWorkspaceScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingCodeIntelligence : IWorkbenchCodeIntelligenceService
    {
        internal WorkbenchCodeSessionRequest? StartRequest { get; private set; }
        internal WorkbenchCodeInteractiveSnapshot? QuickInfoSnapshot { get; private set; }
        internal bool WasStopped { get; private set; }

        public ValueTask<WorkbenchCodeSessionView> StartAsync(
            WorkbenchCodeSessionRequest request,
            IProgress<WorkbenchCodeLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            StartRequest = request;
            return ValueTask.FromResult(new WorkbenchCodeSessionView(
                new("context-1"), new("session-1"), WorkbenchCodeResultState.Ready, []));
        }

        public ValueTask<WorkbenchCodeDiagnosticView> SynchronizeAsync(
            WorkbenchCodeDocumentSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkbenchCodeDiagnosticView(
                snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready, [], []));

        public ValueTask<WorkbenchCodeQuickInfoView> GetQuickInfoAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            QuickInfoSnapshot = snapshot;
            return ValueTask.FromResult(new WorkbenchCodeQuickInfoView(
                snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready, null, [new("symbol")], []));
        }

        public ValueTask<WorkbenchCodeNavigationView> FindReferencesAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            Navigation(snapshot);

        public ValueTask<WorkbenchCodeNavigationView> FindImplementationsAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => Navigation(snapshot);

        public ValueTask StopAsync(
            WorkbenchCodeSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            WasStopped = true;
            return ValueTask.CompletedTask;
        }

        private static ValueTask<WorkbenchCodeNavigationView> Navigation(
            WorkbenchCodeInteractiveSnapshot snapshot) => ValueTask.FromResult(
            new WorkbenchCodeNavigationView(
                snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                [new(
                    WorkbenchCodeDestinationKind.Source,
                    new("Reference"),
                    new("src/Reference.cs"),
                    new(new(0, 0), new(0, 9)))],
                []));

        public ValueTask<WorkbenchCodeValidationView> ValidateAsync(
            WorkbenchCodeValidationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeCompletionView> GetCompletionsAsync(
            WorkbenchCodeCompletionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeCompletionCommitView> CommitCompletionAsync(
            WorkbenchCodeCompletionCommitRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeSignatureHelpView> GetSignatureHelpAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeNavigationView> FindDefinitionAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => Navigation(snapshot);
    }

    private sealed class StubGoalStore : IGoalStore
    {
        private static readonly StoredGoal Goal = new(
            "goal-1", "workspace-1", "Title", "Objective", 2, null, "Approved",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public ValueTask<StoredGoal?> GetAsync(
            string goalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StoredGoal?>(goalId == Goal.Id ? Goal : null);
        public ValueTask<StoredGoal> CreateAsync(
            StoredGoal goal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(
            string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoal?> UpdateDraftSettingsAsync(
            string goalId, DateTimeOffset expectedUpdatedAt, int reviewCycleLimit,
            long? remoteBudgetMicrousd, DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(
            string extensionId, string goalId, long? expectedBudgetMicrousd,
            long newBudgetMicrousd, string reason, DateTimeOffset approvedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlan?> GetCurrentPlanAsync(
            string goalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> SavePlanAsync(
            StoredPlan plan, string expectedGoalState, string nextGoalState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(
            StoredApproval approval, StoredGoalWorktree? worktree, string expectedGoalState,
            string expectedPlanState, string nextGoalState, string nextPlanState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(
            string goalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubWorkspaceStore : IWorkspaceStore
    {
        private static readonly RegisteredWorkspace Workspace = new(
            "workspace-1", "/workspace/repository", "repository",
            "/workspace/repository/Harness.slnx", true, true, "main", false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(Workspace);
        public ValueTask<RegisteredWorkspace> SaveAsync(
            WorkspaceInspection inspection, string entryPoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(
            string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(
            string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(
            string workspaceId, bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
