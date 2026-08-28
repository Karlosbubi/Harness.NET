using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.DataAccess.Approvals;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Tools;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Mutations;

public sealed class WorkspaceCandidateRepairTests
{
    private const string Baseline =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Original = "class Example { List<string> Values { get; } = []; }";
    private const string Repaired =
        "using System.Collections.Generic;\n\nclass Example { List<string> Values { get; } = []; }";

    [Fact]
    public async Task Unique_compiler_proven_import_repairs_and_records_model_edit()
    {
        FakeCodeIntelligence code = new();
        code.Validations.Enqueue(Validation(
            WorkbenchCodeValidationDisposition.Rejected,
            MissingTypeDiagnostic("CS0246")));
        code.Validations.Enqueue(Validation(WorkbenchCodeValidationDisposition.Validated));
        code.Validations.Enqueue(Validation(WorkbenchCodeValidationDisposition.Validated));
        code.MissingImports = [MissingImport("System.Collections.Generic")];
        FakeFileEditor editor = new();
        FakeEvidenceStore evidence = new();
        WorkspaceMutationService service = Service(code, editor, evidence);

        FileEditView result = await service.ApplyFileEditAsync(Request("unique-import"));

        Assert.Null(result.ErrorCode);
        FileEditDeterministicRepairView repair = Assert.Single(result.DeterministicRepairs);
        Assert.Equal(FileEditDeterministicRepairKind.AddMissingImport, repair.Kind);
        Assert.Equal("CS0246", repair.DiagnosticId.Value);
        Assert.Equal("System.Collections.Generic", repair.Namespace.Value);
        Assert.Equal(Repaired, editor.LastEdit?.Content);
        Assert.Equal(
            [WorkbenchCodeValidationPhase.Candidate, WorkbenchCodeValidationPhase.Candidate,
                WorkbenchCodeValidationPhase.Applied],
            code.Phases);
        Assert.Contains("deterministicRepairs", Assert.Single(evidence.Items).ResultJson,
            StringComparison.Ordinal);
        Assert.Contains("System.Collections.Generic", evidence.Items[0].ResultJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ambiguous_import_keeps_original_fail_closed_result()
    {
        FakeCodeIntelligence code = new();
        code.Validations.Enqueue(Validation(
            WorkbenchCodeValidationDisposition.Rejected,
            MissingTypeDiagnostic("CS0246")));
        code.MissingImports =
        [
            MissingImport("First.Namespace"),
            MissingImport("Second.Namespace"),
        ];
        FakeFileEditor editor = new();
        WorkspaceMutationService service = Service(code, editor, new FakeEvidenceStore());

        FileEditView result = await service.ApplyFileEditAsync(Request("ambiguous-import"));

        Assert.Equal("compiler_validation_rejected", result.ErrorCode);
        Assert.Empty(result.DeterministicRepairs);
        Assert.Null(editor.LastEdit);
        Assert.Equal(0, code.PreviewCallCount);
    }

    [Fact]
    public async Task Candidate_that_remains_invalid_after_import_is_not_written()
    {
        FakeCodeIntelligence code = new();
        code.Validations.Enqueue(Validation(
            WorkbenchCodeValidationDisposition.Rejected,
            MissingTypeDiagnostic("CS0103")));
        code.Validations.Enqueue(Validation(
            WorkbenchCodeValidationDisposition.Rejected,
            IntroducedDiagnostic("CS8600", WorkbenchCodeDiagnosticSeverity.Warning)));
        code.MissingImports = [MissingImport("System.Collections.Generic")];
        FakeFileEditor editor = new();
        WorkspaceMutationService service = Service(code, editor, new FakeEvidenceStore());

        FileEditView result = await service.ApplyFileEditAsync(Request("still-invalid"));

        Assert.Equal("compiler_validation_rejected", result.ErrorCode);
        Assert.Empty(result.DeterministicRepairs);
        Assert.Null(editor.LastEdit);
        Assert.Equal(1, code.PreviewCallCount);
    }

    [Fact]
    public async Task More_than_four_diagnostics_skips_repair_without_semantic_queries()
    {
        FakeCodeIntelligence code = new();
        code.Validations.Enqueue(Validation(
            WorkbenchCodeValidationDisposition.Rejected,
            Enumerable.Range(0, 5)
                .Select(index => MissingTypeDiagnostic("CS0246", index))
                .ToArray()));
        FakeFileEditor editor = new();
        WorkspaceMutationService service = Service(code, editor, new FakeEvidenceStore());

        FileEditView result = await service.ApplyFileEditAsync(Request("bounded-repair"));

        Assert.Equal("compiler_validation_rejected", result.ErrorCode);
        Assert.Empty(result.DeterministicRepairs);
        Assert.Null(editor.LastEdit);
        Assert.Equal(0, code.MissingImportCallCount);
        Assert.Equal(0, code.PreviewCallCount);
    }

    [Fact]
    public async Task Stale_transformation_preview_does_not_write_partial_repair()
    {
        FakeCodeIntelligence code = new() { PreviewState = WorkbenchCodeResultState.Stale };
        code.Validations.Enqueue(Validation(
            WorkbenchCodeValidationDisposition.Rejected,
            MissingTypeDiagnostic("CS0246")));
        code.MissingImports = [MissingImport("System.Collections.Generic")];
        FakeFileEditor editor = new();
        WorkspaceMutationService service = Service(code, editor, new FakeEvidenceStore());

        FileEditView result = await service.ApplyFileEditAsync(Request("stale-repair"));

        Assert.Equal("compiler_validation_rejected", result.ErrorCode);
        Assert.Empty(result.DeterministicRepairs);
        Assert.Null(editor.LastEdit);
    }

    private static WorkspaceMutationService Service(
        FakeCodeIntelligence code,
        FakeFileEditor editor,
        FakeEvidenceStore evidence) => new(
        new GoalStore(),
        new WorkspaceStore(),
        editor,
        new DotNetRunner(),
        evidence,
        new ApprovalStore(),
        code);

    private static FileEditRequest Request(string correlationId) => new(
        "goal-id",
        new(correlationId),
        "src/Example.cs",
        Baseline,
        Original,
        FileEditOrigin.Model);

    private static WorkbenchCodeValidationView Validation(
        WorkbenchCodeValidationDisposition disposition,
        params WorkbenchCodeValidationDiagnostic[] diagnostics) => new(
        new("session-id"),
        WorkbenchCodeResultState.Ready,
        disposition,
        diagnostics,
        []);

    private static WorkbenchCodeValidationDiagnostic MissingTypeDiagnostic(
        string id,
        int line = 0) =>
        IntroducedDiagnostic(id, WorkbenchCodeDiagnosticSeverity.Error, line);

    private static WorkbenchCodeValidationDiagnostic IntroducedDiagnostic(
        string id,
        WorkbenchCodeDiagnosticSeverity severity,
        int line = 0) => new(
        WorkbenchCodeDiagnosticDeltaKind.Introduced,
        new(
            new(id),
            new("Introduced diagnostic."),
            new("Compiler"),
            new("Project"),
            new("src/Example.cs"),
            new(new(line, 16), new(line, 20)),
            severity));

    private static WorkbenchCodeMissingImportCandidate MissingImport(string value) => new(
        new(value),
        new(value + ".List<T>"),
        new(new(0, 16), new(0, 20)));

    private sealed class FakeCodeIntelligence : IWorkbenchCodeIntelligenceService
    {
        internal Queue<WorkbenchCodeValidationView> Validations { get; } = [];
        internal IReadOnlyList<WorkbenchCodeMissingImportCandidate> MissingImports { get; set; } = [];
        internal WorkbenchCodeResultState PreviewState { get; init; } = WorkbenchCodeResultState.Ready;
        internal List<WorkbenchCodeValidationPhase> Phases { get; } = [];
        internal int MissingImportCallCount { get; private set; }
        internal int PreviewCallCount { get; private set; }

        public ValueTask<WorkbenchCodeSessionView> StartAsync(
            WorkbenchCodeSessionRequest request,
            IProgress<WorkbenchCodeLoadProgress>? progress = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new
                WorkbenchCodeSessionView(new("context-id"), new("session-id"),
                    WorkbenchCodeResultState.Ready, []));

        public ValueTask<WorkbenchCodeValidationView> ValidateAsync(
            WorkbenchCodeValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            Phases.Add(request.Phase);
            return ValueTask.FromResult(Validations.Dequeue());
        }

        public ValueTask<WorkbenchCodeMissingImportView> GetMissingImportsAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            MissingImportCallCount++;
            return ValueTask.FromResult(new WorkbenchCodeMissingImportView(
                snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready, MissingImports, []));
        }

        public ValueTask<WorkbenchCodeDocumentTransformationPreviewView>
            PreviewDocumentTransformationAsync(
                WorkbenchCodeDocumentTransformationPreviewRequest request,
                CancellationToken cancellationToken = default)
        {
            PreviewCallCount++;
            return ValueTask.FromResult(new WorkbenchCodeDocumentTransformationPreviewView(
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                PreviewState,
                WorkbenchCodeTransformationDisposition.Ready,
                request.Kind,
                request.Range,
                [new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                    request.Snapshot.Text, new(Repaired), 1)],
                [],
                [],
                new(Baseline),
                [],
                request.ImportNamespace));
        }

        public ValueTask<WorkbenchCodeDiagnosticView> SynchronizeAsync(
            WorkbenchCodeDocumentSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public ValueTask StopAsync(
            WorkbenchCodeSessionId sessionId,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeFileEditor : IWorkspaceFileEditor
    {
        internal WorkspaceFileEdit? LastEdit { get; private set; }

        public ValueTask<WorkspaceFileEditResult> ApplyAsync(
            string worktreeRoot,
            WorkspaceFileEdit edit,
            CancellationToken cancellationToken = default)
        {
            LastEdit = edit;
            return ValueTask.FromResult(new WorkspaceFileEditResult(
                edit.Path, edit.ExpectedSha256, "new-hash", edit.Content.Length,
                WasCreated: false, ErrorCode: null, Error: null));
        }

        public ValueTask<WorkspaceFileBatchEditResult> ApplyBatchAsync(
            string worktreeRoot,
            WorkspaceFileBatchEdit batch,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeEvidenceStore : IToolEvidenceStore
    {
        internal List<StoredToolCall> Items { get; } = [];

        public ValueTask<StoredToolCallStart> StartAsync(
            StoredToolCall toolCall,
            CancellationToken cancellationToken = default)
        {
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
            int index = Items.FindIndex(item => item.Id == toolCallId);
            StoredToolCall item = Items[index] with
            {
                State = nextState,
                ResultJson = resultJson,
                CompletedAt = completedAt,
            };
            Items[index] = item;
            return ValueTask.FromResult(item);
        }

        public ValueTask<IReadOnlyList<StoredToolCall>> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredToolCall>>(Items);
    }

    private sealed class GoalStore : IGoalStore
    {
        private static readonly StoredGoal Goal = new(
            "goal-id", "workspace-id", "Goal", "Objective", 3, null, "Approved",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        private static readonly StoredGoalWorktree Worktree = new(
            "goal-id", "workspace-id", "harness/goal", "/worktree", "head", "Active",
            DateTimeOffset.UtcNow);

        public ValueTask<StoredGoal?> GetAsync(string goalId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<StoredGoal?>(Goal);
        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StoredGoalWorktree?>(Worktree);
        public ValueTask<StoredGoal> CreateAsync(StoredGoal value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoal?> UpdateDraftSettingsAsync(string goalId,
            DateTimeOffset expectedUpdatedAt, int reviewCycleLimit, long? remoteBudgetMicrousd,
            DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(
            string extensionId, string goalId, long? expectedBudgetMicrousd,
            long newBudgetMicrousd, string reason, DateTimeOffset approvedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlan?> GetCurrentPlanAsync(string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> SavePlanAsync(StoredPlan plan,
            string expectedGoalState, string nextGoalState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(StoredApproval approval,
            StoredGoalWorktree? value, string expectedGoalState, string expectedPlanState,
            string nextGoalState, string nextPlanState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class WorkspaceStore : IWorkspaceStore
    {
        private static readonly RegisteredWorkspace Workspace = new(
            "workspace-id", "/workspace", "workspace", "/workspace/Harness.slnx",
            IsTrusted: true, IsActive: true, "main", IsDirty: false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(Workspace);
        public ValueTask<RegisteredWorkspace> SaveAsync(WorkspaceInspection inspection,
            string entryPoint, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(string rootPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(string workspaceId, bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DotNetRunner : IDotNetToolRunner
    {
        public ValueTask<DotNetToolResult> RunAsync(string worktreeRoot,
            DotNetToolRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ApprovalStore : ICapabilityApprovalStore
    {
        public ValueTask<StoredCapabilityApproval?> GetAsync(string goalId,
            Harness.DataAccess.Tools.ToolCorrelationId correlationId,
            CapabilityKind capability, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StoredCapabilityApproval?>(null);
        public ValueTask<StoredCapabilityApprovalStart> StartAsync(
            StoredCapabilityApproval value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StoredCapabilityApproval> DecideAsync(CapabilityApprovalId approvalId,
            CapabilityApprovalState expectedState, CapabilityApprovalState nextState,
            string? decisionReason, DateTimeOffset decidedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredCapabilityApproval?> GetByIdAsync(CapabilityApprovalId approvalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredCapabilityApproval>> ListAsync(string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
