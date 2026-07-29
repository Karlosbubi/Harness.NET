using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Documents;

public sealed class WorkbenchDocumentServiceTests
{
    [Fact]
    public async Task Approved_goal_opens_the_isolated_worktree_with_an_exact_baseline()
    {
        FileReader reader = new(new("src/App.cs", "content", 7, false, null, null));
        WorkbenchDocumentService service = CreateService(
            CreateGoal("Approved"),
            CreateWorktree(),
            CreateWorkspace(isTrusted: true),
            reader,
            new MutationService());

        WorkbenchDocumentView result = await service.OpenAsync(new(
            new("workspace-id"),
            new("goal-id"),
            new("src/App.cs")));

        Assert.Null(result.Error);
        Assert.Equal(WorkbenchDocumentAccess.Editable, result.Access);
        Assert.Equal("/state/worktrees/goal-id", reader.Root);
        Assert.Equal("harness/goal-test", result.Branch?.Value);
        Assert.Equal(Hash("content"), result.Sha256?.Value);
        Assert.Contains("Approved goal worktree", result.AccessDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Goal_without_an_active_approval_opens_only_the_original_read_only_file()
    {
        FileReader reader = new(new("src/App.cs", "content", 7, false, null, null));
        WorkbenchDocumentService service = CreateService(
            CreateGoal("AwaitingPlanApproval"),
            worktree: null,
            CreateWorkspace(isTrusted: true),
            reader,
            new MutationService());

        WorkbenchDocumentView result = await service.OpenAsync(new(
            new("workspace-id"),
            new("goal-id"),
            new("src/App.cs")));

        Assert.Equal(WorkbenchDocumentAccess.ReadOnly, result.Access);
        Assert.Null(result.GoalId);
        Assert.Equal("/workspace/repository", reader.Root);
        Assert.Contains("Approve", result.AccessDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Truncated_worktree_source_is_honestly_read_only_without_a_baseline()
    {
        FileReader reader = new(new("large.cs", "partial", 70_000, true, null, null));
        WorkbenchDocumentService service = CreateService(
            CreateGoal("Approved"),
            CreateWorktree(),
            CreateWorkspace(isTrusted: true),
            reader,
            new MutationService());

        WorkbenchDocumentView result = await service.OpenAsync(new(
            new("workspace-id"),
            new("goal-id"),
            new("large.cs")));

        Assert.Equal(WorkbenchDocumentAccess.ReadOnly, result.Access);
        Assert.True(result.IsTruncated);
        Assert.Null(result.Sha256);
        Assert.Contains("complete file was not loaded", result.AccessDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_maps_the_semantic_request_through_the_durable_mutation_boundary()
    {
        MutationService mutations = new()
        {
            Result = new(
                "goal-id",
                new("desktop-edit-1"),
                "src/App.cs",
                Hash("before"),
                Hash("after"),
                5,
                WasCreated: false,
                ErrorCode: null,
                Error: null),
        };
        WorkbenchDocumentService service = CreateService(
            CreateGoal("Approved"),
            CreateWorktree(),
            CreateWorkspace(isTrusted: true),
            new(new("src/App.cs", "before", 6, false, null, null)),
            mutations);

        WorkbenchDocumentSaveResult result = await service.SaveAsync(new(
            new("goal-id"),
            new("desktop-edit-1"),
            new("src/App.cs"),
            new(Hash("before")),
            new("after")));

        Assert.Equal(WorkbenchDocumentSaveOutcome.Saved, result.Outcome);
        Assert.Equal(Hash("after"), result.SavedSha256?.Value);
        Assert.Equal("after", mutations.Request?.Content);
        Assert.Equal(Hash("before"), mutations.Request?.ExpectedSha256);
    }

    [Fact]
    public async Task Compare_and_swap_failure_returns_the_current_version_as_an_actionable_conflict()
    {
        MutationService mutations = new()
        {
            Result = new(
                "goal-id",
                new("desktop-edit-2"),
                "src/App.cs",
                Hash("external"),
                null,
                0,
                WasCreated: false,
                "content_changed",
                "The file changed."),
        };
        WorkbenchDocumentService service = CreateService(
            CreateGoal("Approved"),
            CreateWorktree(),
            CreateWorkspace(isTrusted: true),
            new(new("src/App.cs", "before", 6, false, null, null)),
            mutations);

        WorkbenchDocumentSaveResult result = await service.SaveAsync(new(
            new("goal-id"),
            new("desktop-edit-2"),
            new("src/App.cs"),
            new(Hash("before")),
            new("after")));

        Assert.Equal(WorkbenchDocumentSaveOutcome.Conflict, result.Outcome);
        Assert.Equal(Hash("external"), result.CurrentSha256?.Value);
        Assert.Null(result.SavedSha256);
    }

    [Fact]
    public async Task Revoked_trust_rejects_open_before_any_file_access()
    {
        FileReader reader = new(new("src/App.cs", "content", 7, false, null, null));
        WorkbenchDocumentService service = CreateService(
            CreateGoal("Approved"),
            CreateWorktree(),
            CreateWorkspace(isTrusted: false),
            reader,
            new MutationService());

        WorkbenchDocumentView result = await service.OpenAsync(new(
            new("workspace-id"),
            new("goal-id"),
            new("src/App.cs")));

        Assert.Equal("workspace_not_trusted", result.ErrorCode);
        Assert.Equal(0, reader.CallCount);
    }

    private static WorkbenchDocumentService CreateService(
        StoredGoal goal,
        StoredGoalWorktree? worktree,
        RegisteredWorkspace workspace,
        FileReader reader,
        MutationService mutations) => new(
        new WorkbenchWorkspaceContextResolver(
            new GoalStore(goal, worktree),
            new WorkspaceStore(workspace)),
        reader,
        mutations);

    private static StoredGoal CreateGoal(string state) => new(
        "goal-id",
        "workspace-id",
        "Safe edit",
        "Edit one source file",
        2,
        RemoteBudgetMicrousd: null,
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
        "/workspace/repository/Harness.slnx",
        isTrusted,
        IsActive: true,
        "main",
        IsDirty: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class FileReader(WorkspaceFileRead result) : IWorkspaceFileReader
    {
        internal int CallCount { get; private set; }
        internal string? Root { get; private set; }

        public ValueTask<WorkspaceFileRead> ReadAsync(
            string workspaceRoot,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Root = workspaceRoot;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class MutationService : IWorkspaceMutationService
    {
        internal FileEditRequest? Request { get; private set; }
        internal FileEditView? Result { get; init; }

        public ValueTask<FileEditView> ApplyFileEditAsync(
            FileEditRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(Result ?? new FileEditView(
                request.GoalId,
                request.CorrelationId,
                request.Path,
                request.ExpectedSha256,
                Hash(request.Content),
                Encoding.UTF8.GetByteCount(request.Content),
                WasCreated: false,
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<DotNetOperationView> RunDotNetAsync(
            DotNetOperationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class GoalStore(
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

    private sealed class WorkspaceStore(RegisteredWorkspace workspace) : IWorkspaceStore
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
}
