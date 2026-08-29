using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.DataAccess.Approvals;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Mutations;

public sealed partial class WorkspaceMutationServiceTests
{
    private static WorkbenchCodeValidationDiagnostic IntroducedDiagnostic(
        WorkbenchCodeDiagnosticSeverity severity) => new(
        WorkbenchCodeDiagnosticDeltaKind.Introduced,
        new(
            new("CS0001"),
            new("Introduced diagnostic."),
            new("Compiler"),
            new("Project"),
            new("Program.cs"),
            new(new(0, 0), new(0, 1)),
            severity));

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

    private static RenameSymbolPreviewRequest RenameRequest(
        RenameSymbolOrigin origin,
        IReadOnlyList<RenameFileArea> areas) => new(
        "goal-id",
        new("src/First.cs"),
        new(Baseline),
        new(1),
        new("class First { }"),
        new(0, 7),
        new("Renamed"),
        origin,
        areas);

    private static DocumentTransformationPreviewRequest DocumentTransformationRequest(
        DocumentTransformationOrigin origin,
        IReadOnlyList<DocumentTransformationFileArea> areas) => new(
        "goal-id",
        new("src/First.cs"),
        new(Baseline),
        new(1),
        new("class First{ }"),
        new(0, 0),
        WorkbenchCodeDocumentTransformationKind.FormatDocument,
        Range: null,
        origin,
        areas);

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

    private static StoredCapabilityApproval CreateRestoreApproval(
        string correlationId,
        string target,
        CapabilityApprovalState state = CapabilityApprovalState.Approved) => new(
        new("approval-id"),
        "goal-id",
        new(correlationId),
        CapabilityKind.Restore,
        target,
        "Packages are required for the approved plan.",
        state,
        DecisionReason: state is CapabilityApprovalState.Denied ? "Denied." : null,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class FakeFileEditor : IWorkspaceFileEditor
    {
        internal int CallCount { get; private set; }
        internal int BatchCallCount { get; private set; }
        internal string? Root { get; private set; }
        internal WorkspaceFileBatchEdit? LastBatch { get; private set; }
        internal Exception? Exception { get; init; }
        internal bool OmitLastBatchResult { get; init; }

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

        public ValueTask<WorkspaceFileBatchEditResult> ApplyBatchAsync(
            string worktreeRoot,
            WorkspaceFileBatchEdit batch,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            Root = worktreeRoot;
            LastBatch = batch;
            IReadOnlyList<WorkspaceFileEdit> confirmed = OmitLastBatchResult
                ? batch.Edits.SkipLast(1).ToArray()
                : batch.Edits;
            return ValueTask.FromResult(new WorkspaceFileBatchEditResult(
                confirmed.Select(edit => new WorkspaceFileEditResult(
                    edit.Path,
                    edit.ExpectedSha256,
                    Baseline,
                    edit.Content.Length,
                    WasCreated: false,
                    ErrorCode: null,
                    Error: null)).ToArray(),
                WasRolledBack: false,
                WasCancelled: false,
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

    private sealed class FakeCapabilityApprovalStore(
        StoredCapabilityApproval? approval = null) : ICapabilityApprovalStore
    {
        public ValueTask<StoredCapabilityApproval?> GetAsync(
            string goalId,
            Harness.DataAccess.Tools.ToolCorrelationId correlationId,
            CapabilityKind capability,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                approval?.GoalId == goalId &&
                approval.CorrelationId == correlationId &&
                approval.Capability == capability
                    ? approval
                    : null);

        public ValueTask<StoredCapabilityApprovalStart> StartAsync(StoredCapabilityApproval value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredCapabilityApproval> DecideAsync(CapabilityApprovalId approvalId, CapabilityApprovalState expectedState, CapabilityApprovalState nextState, string? decisionReason, DateTimeOffset decidedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredCapabilityApproval?> GetByIdAsync(CapabilityApprovalId approvalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredCapabilityApproval>> ListAsync(string goalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCodeIntelligenceService : IWorkbenchCodeIntelligenceService
    {
        private static readonly WorkbenchCodeSessionId SessionId = new("session-id");

        internal int StartCallCount { get; private set; }
        internal List<WorkbenchCodeValidationPhase> Phases { get; } = [];
        internal WorkbenchCodeValidationDisposition CandidateDisposition { get; init; } =
            WorkbenchCodeValidationDisposition.Validated;
        internal WorkbenchCodeValidationDisposition AppliedDisposition { get; init; } =
            WorkbenchCodeValidationDisposition.Validated;
        internal IReadOnlyList<WorkbenchCodeValidationDiagnostic> CandidateDiagnostics { get; init; } = [];
        internal IReadOnlyList<WorkbenchCodeValidationDiagnostic> AppliedDiagnostics { get; init; } = [];
        internal string RenameFingerprint { get; init; } = Baseline;
        internal string DocumentTransformationFingerprint { get; init; } = Baseline;
        internal IReadOnlyList<WorkbenchCodeDocumentTransformationEdit>?
            DocumentTransformationEdits
        { get; init; }
        internal WorkbenchCodeValidationRequest? LastValidationRequest { get; private set; }

        public ValueTask<WorkbenchCodeSessionView> StartAsync(
            WorkbenchCodeSessionRequest request,
            IProgress<WorkbenchCodeLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            return ValueTask.FromResult(new WorkbenchCodeSessionView(
                new("context-id"),
                SessionId,
                WorkbenchCodeResultState.Ready,
                []));
        }

        public ValueTask<WorkbenchCodeValidationView> ValidateAsync(
            WorkbenchCodeValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            Phases.Add(request.Phase);
            LastValidationRequest = request;
            WorkbenchCodeValidationDisposition disposition =
                request.Phase is WorkbenchCodeValidationPhase.Candidate
                    ? CandidateDisposition
                    : AppliedDisposition;
            return ValueTask.FromResult(new WorkbenchCodeValidationView(
                request.SessionId,
                WorkbenchCodeResultState.Ready,
                disposition,
                request.Phase is WorkbenchCodeValidationPhase.Candidate
                    ? CandidateDiagnostics
                    : AppliedDiagnostics,
                []));
        }

        public ValueTask<WorkbenchCodeDiagnosticView> SynchronizeAsync(
            WorkbenchCodeDocumentSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkbenchCodeCompletionView> GetCompletionsAsync(
            WorkbenchCodeCompletionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeCompletionCommitView> CommitCompletionAsync(
            WorkbenchCodeCompletionCommitRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeQuickInfoView> GetQuickInfoAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeSignatureHelpView> GetSignatureHelpAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeNavigationView> FindDefinitionAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkbenchCodeNavigationView> FindReferencesAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkbenchCodeRenamePreviewView> PreviewRenameAsync(
            WorkbenchCodeRenamePreviewRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkbenchCodeRenamePreviewView(
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                WorkbenchCodeTransformationDisposition.Ready,
                new("Class|First"),
                request.NewName,
                [
                    new(new("src/First.cs"), new(Baseline), new("class First { }"),
                        new("class Renamed { }"), 1),
                    new(new("tests/Second.cs"), new(Baseline), new("class Use { First value; }"),
                        new("class Use { Renamed value; }"), 1),
                ],
                [],
                [],
                new(RenameFingerprint),
                []));

        public ValueTask<WorkbenchCodeDocumentTransformationPreviewView>
            PreviewDocumentTransformationAsync(
                WorkbenchCodeDocumentTransformationPreviewRequest request,
                CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkbenchCodeDocumentTransformationPreviewView(
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                WorkbenchCodeTransformationDisposition.Ready,
                request.Kind,
                request.Range,
                DocumentTransformationEdits ?? [new(
                    request.Snapshot.Path,
                    request.Snapshot.BaselineHash,
                    request.Snapshot.Text,
                    new("class First { }"),
                    1)],
                [],
                [],
                new(DocumentTransformationFingerprint),
                []));

        public ValueTask StopAsync(
            WorkbenchCodeSessionId sessionId,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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
        public ValueTask<StoredGoal?> UpdateDraftSettingsAsync(string goalId, DateTimeOffset expectedUpdatedAt, int reviewCycleLimit, long? remoteBudgetMicrousd, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(string extensionId, string goalId, long? expectedBudgetMicrousd, long newBudgetMicrousd, string reason, DateTimeOffset approvedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
