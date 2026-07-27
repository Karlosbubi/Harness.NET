using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
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
        FakeToolEvidenceStore evidence = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            evidence);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("tool-call-42"),
            "Program.cs",
            "expected",
            "replacement"));

        Assert.Null(result.Error);
        Assert.Equal("tool-call-42", result.CorrelationId.Value);
        Assert.Equal("/state/worktrees/goal-id", editor.Root);
        Assert.Equal(1, editor.CallCount);
        Assert.Equal(ToolCallState.Succeeded, Assert.Single(evidence.Items).State);
        Assert.Contains("replacement", evidence.Items[0].RequestJson, StringComparison.Ordinal);
        Assert.NotNull(evidence.Items[0].ResultJson);
    }

    [Fact]
    public async Task Goal_without_an_active_grant_cannot_edit()
    {
        FakeFileEditor editor = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Draft"), worktree: null),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore());

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("tool-call-43"),
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
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore());

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("tool-call-44"),
            "Program.cs",
            null,
            "replacement"));

        Assert.Equal("workspace_not_trusted", result.ErrorCode);
        Assert.Equal(0, editor.CallCount);
    }

    [Fact]
    public async Task Approved_goal_runs_dotnet_in_its_worktree_with_the_registered_entry_point()
    {
        FakeDotNetToolRunner runner = new();
        FakeToolEvidenceStore evidence = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileEditor(),
            runner,
            evidence);

        DotNetOperationView result = await service.RunDotNetAsync(new(
            "goal-id",
            new("tool-call-45"),
            DotNetOperation.Build));

        Assert.Null(result.Error);
        Assert.Equal("tool-call-45", result.CorrelationId.Value);
        Assert.Equal("/state/worktrees/goal-id", runner.Root);
        Assert.Equal("Repository.slnx", runner.Request?.EntryPoint);
        Assert.Equal(DotNetToolOperation.Build, runner.Request?.Operation);
        Assert.Equal(ToolCallState.Succeeded, Assert.Single(evidence.Items).State);
        Assert.Contains("\"operation\":\"Build\"", evidence.Items[0].RequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dotnet_execution_requires_an_active_approved_grant()
    {
        FakeDotNetToolRunner runner = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Planned"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileEditor(),
            runner,
            new FakeToolEvidenceStore());

        DotNetOperationView result = await service.RunDotNetAsync(new(
            "goal-id",
            new("tool-call-46"),
            DotNetOperation.Test));

        Assert.Equal("goal_not_approved", result.ErrorCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task Cancelled_dotnet_execution_is_durably_completed_as_cancelled()
    {
        FakeDotNetToolRunner runner = new() { WasCancelled = true };
        FakeToolEvidenceStore evidence = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileEditor(),
            runner,
            evidence);

        DotNetOperationView result = await service.RunDotNetAsync(new(
            "goal-id",
            new("tool-call-cancelled"),
            DotNetOperation.Test));

        Assert.True(result.WasCancelled);
        Assert.Equal(ToolCallState.Cancelled, Assert.Single(evidence.Items).State);
        Assert.NotNull(evidence.Items[0].CompletedAt);
    }

    [Fact]
    public async Task Duplicate_correlation_is_rejected_before_tool_execution()
    {
        FakeFileEditor editor = new();
        FakeToolEvidenceStore evidence = new();
        evidence.Items.Add(new StoredToolCall(
            new("existing-id"),
            "goal-id",
            new("tool-call-47"),
            ToolKind.FileEdit,
            "{}",
            ToolCallState.Succeeded,
            "{}",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            evidence);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("tool-call-47"),
            "Program.cs",
            null,
            "replacement"));

        Assert.Equal("duplicate_correlation", result.ErrorCode);
        Assert.Equal(0, editor.CallCount);
    }

    [Fact]
    public async Task Interrupted_tool_retains_its_running_evidence_for_recovery()
    {
        FakeFileEditor editor = new() { Exception = new OperationCanceledException() };
        FakeToolEvidenceStore evidence = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            evidence);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplyFileEditAsync(new(
            "goal-id",
            new("tool-call-interrupted"),
            "Program.cs",
            null,
            "replacement")).AsTask());

        StoredToolCall item = Assert.Single(evidence.Items);
        Assert.Equal(ToolCallState.Running, item.State);
        Assert.Null(item.ResultJson);
        Assert.Null(item.CompletedAt);
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
        internal Exception? Exception { get; init; }

        public ValueTask<WorkspaceFileEditResult> ApplyAsync(
            string worktreeRoot,
            WorkspaceFileEdit edit,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Root = worktreeRoot;
            if (Exception is not null)
            {
                throw Exception;
            }

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

    private sealed class FakeDotNetToolRunner : IDotNetToolRunner
    {
        internal int CallCount { get; private set; }
        internal string? Root { get; private set; }
        internal DotNetToolRequest? Request { get; private set; }
        internal bool WasCancelled { get; init; }

        public ValueTask<DotNetToolResult> RunAsync(
            string worktreeRoot,
            DotNetToolRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Root = worktreeRoot;
            Request = request;
            return ValueTask.FromResult(new DotNetToolResult(
                request.Operation,
                request.EntryPoint,
                0,
                "Build succeeded.",
                string.Empty,
                IsOutputTruncated: false,
                IsErrorTruncated: false,
                WasCancelled,
                DurationMilliseconds: 10,
                ErrorCode: WasCancelled ? "cancelled" : null,
                Error: WasCancelled ? "The operation was cancelled." : null));
        }
    }

    private sealed class FakeToolEvidenceStore : IToolEvidenceStore
    {
        internal List<StoredToolCall> Items { get; } = [];

        public ValueTask<StoredToolCallStart> StartAsync(
            StoredToolCall toolCall,
            CancellationToken cancellationToken = default)
        {
            StoredToolCall? existing = Items.SingleOrDefault(item =>
                item.GoalId == toolCall.GoalId &&
                item.CorrelationId == toolCall.CorrelationId);
            if (existing is not null)
            {
                return ValueTask.FromResult(new StoredToolCallStart(existing, WasCreated: false));
            }

            Items.Add(toolCall);
            return ValueTask.FromResult(new StoredToolCallStart(toolCall, WasCreated: true));
        }

        public ValueTask<StoredToolCall> CompleteAsync(
            ToolCallId toolCallId,
            ToolCallState expectedState,
            ToolCallState nextState,
            string resultJson,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            int index = Items.FindIndex(item => item.Id == toolCallId && item.State == expectedState);
            if (index < 0)
            {
                throw new InvalidOperationException();
            }

            StoredToolCall completed = Items[index] with
            {
                State = nextState,
                ResultJson = resultJson,
                CompletedAt = completedAt,
            };
            Items[index] = completed;
            return ValueTask.FromResult(completed);
        }

        public ValueTask<IReadOnlyList<StoredToolCall>> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredToolCall>>(
                Items.Where(item => item.GoalId == goalId).ToArray());
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
