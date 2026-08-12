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

public sealed class WorkspaceMutationServiceTests
{
    private const string Baseline =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

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
            evidence,
            new FakeCapabilityApprovalStore());

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
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore());

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
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore());

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
    public async Task Model_compiler_edit_is_rejected_before_writing_when_validation_rejects_it()
    {
        FakeFileEditor editor = new();
        FakeToolEvidenceStore evidence = new();
        FakeCodeIntelligenceService codeIntelligence = new()
        {
            CandidateDisposition = WorkbenchCodeValidationDisposition.Rejected,
        };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            evidence,
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("model-rejected"),
            "Program.cs",
            Baseline,
            "class Program { int value = ; }",
            FileEditOrigin.Model));

        Assert.Equal("compiler_validation_rejected", result.ErrorCode);
        Assert.Equal(0, editor.CallCount);
        Assert.Equal([WorkbenchCodeValidationPhase.Candidate], codeIntelligence.Phases);
        Assert.Equal(ToolCallState.Failed, Assert.Single(evidence.Items).State);
        Assert.Contains(
            "candidateCodeValidation",
            evidence.Items[0].ResultJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Model_compiler_edit_is_rejected_before_writing_when_it_introduces_a_warning()
    {
        FakeFileEditor editor = new();
        FakeCodeIntelligenceService codeIntelligence = new()
        {
            CandidateDiagnostics = [IntroducedDiagnostic(WorkbenchCodeDiagnosticSeverity.Warning)],
        };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("model-warning"),
            "Program.cs",
            Baseline,
            "class Program { }",
            FileEditOrigin.Model));

        Assert.Equal("compiler_validation_rejected", result.ErrorCode);
        Assert.Equal(0, editor.CallCount);
        Assert.Equal([WorkbenchCodeValidationPhase.Candidate], codeIntelligence.Phases);
    }

    [Theory]
    [InlineData("class Program { // TODO: implement\n}")]
    [InlineData("class Program { /* placeholder logic */ }")]
    [InlineData("class Program { // Add your implementation logic here\n}")]
    [InlineData("class Program { void Run() => throw new NotImplementedException(); }")]
    public async Task Model_compiler_edit_with_explicit_incomplete_marker_is_rejected_before_validation(
        string content)
    {
        FakeFileEditor editor = new();
        FakeToolEvidenceStore evidence = new();
        FakeCodeIntelligenceService codeIntelligence = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            evidence,
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("model-incomplete"),
            "Program.cs",
            Baseline,
            content,
            FileEditOrigin.Model));

        Assert.Equal("incomplete_model_edit_rejected", result.ErrorCode);
        Assert.Equal(0, editor.CallCount);
        Assert.Equal(0, codeIntelligence.StartCallCount);
        Assert.Equal(ToolCallState.Failed, Assert.Single(evidence.Items).State);
    }

    [Fact]
    public async Task Human_compiler_edit_may_contain_an_incomplete_marker()
    {
        FakeFileEditor editor = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            new FakeCodeIntelligenceService());

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("human-incomplete"),
            "Program.cs",
            Baseline,
            "class Program { // TODO: user-owned work\n}",
            FileEditOrigin.Human));

        Assert.Null(result.ErrorCode);
        Assert.Equal(1, editor.CallCount);
    }

    [Fact]
    public async Task Model_compiler_edit_is_checked_before_and_after_the_atomic_write()
    {
        FakeFileEditor editor = new();
        FakeToolEvidenceStore evidence = new();
        FakeCodeIntelligenceService codeIntelligence = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            evidence,
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("model-validated"),
            "Program.cs",
            Baseline,
            "class Program { }",
            FileEditOrigin.Model));

        Assert.Null(result.ErrorCode);
        Assert.Equal(1, editor.CallCount);
        Assert.Equal(
            [WorkbenchCodeValidationPhase.Candidate, WorkbenchCodeValidationPhase.Applied],
            codeIntelligence.Phases);
        Assert.Equal(
            WorkbenchCodeValidationDisposition.Validated,
            result.CandidateCodeValidation?.Disposition);
        Assert.Equal(
            WorkbenchCodeValidationDisposition.Validated,
            result.AppliedCodeValidation?.Disposition);
        Assert.Equal(ToolCallState.Succeeded, Assert.Single(evidence.Items).State);
    }

    [Fact]
    public async Task Model_documentation_edit_records_not_applicable_without_loading_roslyn()
    {
        FakeFileEditor editor = new();
        FakeCodeIntelligenceService codeIntelligence = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("model-docs"),
            "README.md",
            null,
            "# Updated",
            FileEditOrigin.Model));

        Assert.Null(result.ErrorCode);
        Assert.Equal(WorkbenchCodeValidationDisposition.NotApplicable,
            result.CandidateCodeValidation?.Disposition);
        Assert.Equal(0, codeIntelligence.StartCallCount);
        Assert.Equal(1, editor.CallCount);
    }

    [Fact]
    public async Task Post_apply_validation_failure_is_durable_failed_evidence()
    {
        FakeToolEvidenceStore evidence = new();
        FakeCodeIntelligenceService codeIntelligence = new()
        {
            AppliedDisposition = WorkbenchCodeValidationDisposition.Rejected,
        };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileEditor(),
            new FakeDotNetToolRunner(),
            evidence,
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        FileEditView result = await service.ApplyFileEditAsync(new(
            "goal-id",
            new("model-post-failed"),
            "Program.cs",
            Baseline,
            "class Program { }",
            FileEditOrigin.Model));

        Assert.Equal("post_apply_validation_failed", result.ErrorCode);
        Assert.NotNull(result.NewSha256);
        Assert.Equal(ToolCallState.Failed, Assert.Single(evidence.Items).State);
    }

    [Fact]
    public async Task Fingerprinted_rename_applies_one_atomic_batch_and_records_post_validation()
    {
        FakeFileEditor editor = new();
        FakeToolEvidenceStore evidence = new();
        FakeCodeIntelligenceService codeIntelligence = new() { RenameFingerprint = Baseline };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            evidence,
            new FakeCapabilityApprovalStore(),
            codeIntelligence);
        RenameSymbolPreviewRequest previewRequest = RenameRequest(
            RenameSymbolOrigin.Human, []);

        RenameSymbolApplyView result = await service.ApplyRenameAsync(new(
            previewRequest,
            new("rename-apply"),
            new(Baseline)));

        Assert.Null(result.ErrorCode);
        Assert.Equal(1, editor.BatchCallCount);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(WorkbenchCodeValidationDisposition.Validated,
            result.AppliedCodeValidation?.Disposition);
        Assert.Equal([WorkbenchCodeValidationPhase.Applied], codeIntelligence.Phases);
        StoredToolCall item = Assert.Single(evidence.Items);
        Assert.Equal(ToolKind.Rename, item.Tool);
        Assert.Equal(ToolCallState.Succeeded, item.State);
        Assert.Contains("fingerprint", item.RequestJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("originalText", item.ResultJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_rename_fingerprint_is_rejected_before_the_batch_boundary()
    {
        FakeFileEditor editor = new();
        FakeCodeIntelligenceService codeIntelligence = new() { RenameFingerprint = Baseline };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        RenameSymbolApplyView result = await service.ApplyRenameAsync(new(
            RenameRequest(RenameSymbolOrigin.Human, []),
            new("rename-stale"),
            new(new string('a', 64))));

        Assert.Equal("preview_changed", result.ErrorCode);
        Assert.Equal(0, editor.BatchCallCount);
    }

    [Fact]
    public async Task Implementer_rename_fails_closed_when_any_affected_path_is_out_of_grant()
    {
        FakeFileEditor editor = new();
        FakeCodeIntelligenceService codeIntelligence = new() { RenameFingerprint = Baseline };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        RenameSymbolApplyView result = await service.ApplyRenameAsync(new(
            RenameRequest(RenameSymbolOrigin.Model, [new("src")]),
            new("rename-denied"),
            new(Baseline)));

        Assert.Equal("task_file_area_denied", result.ErrorCode);
        Assert.Equal(0, editor.BatchCallCount);
    }

    [Fact]
    public async Task Fingerprinted_document_transformation_applies_atomically_and_records_validation()
    {
        FakeFileEditor editor = new();
        FakeToolEvidenceStore evidence = new();
        FakeCodeIntelligenceService codeIntelligence = new()
        {
            DocumentTransformationFingerprint = Baseline,
        };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            evidence,
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        DocumentTransformationApplyView result =
            await service.ApplyDocumentTransformationAsync(new(
                DocumentTransformationRequest(DocumentTransformationOrigin.Human, []),
                new("format-apply"),
                new(Baseline)));

        Assert.Null(result.ErrorCode);
        Assert.Equal(1, editor.BatchCallCount);
        Assert.Single(result.Files);
        Assert.Equal(WorkbenchCodeValidationDisposition.Validated,
            result.AppliedCodeValidation?.Disposition);
        StoredToolCall item = Assert.Single(evidence.Items);
        Assert.Equal(ToolKind.DocumentTransformation, item.Tool);
        Assert.Equal(ToolCallState.Succeeded, item.State);
        Assert.Contains("FormatDocument", item.RequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Model_document_transformation_fails_closed_outside_its_file_grant()
    {
        FakeFileEditor editor = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            new FakeCodeIntelligenceService());

        DocumentTransformationApplyView result =
            await service.ApplyDocumentTransformationAsync(new(
                DocumentTransformationRequest(
                    DocumentTransformationOrigin.Model, [new("tests")]),
                new("format-denied"),
                new(Baseline)));

        Assert.Equal("task_file_area_denied", result.ErrorCode);
        Assert.Equal(0, editor.BatchCallCount);
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
            evidence,
            new FakeCapabilityApprovalStore());

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
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore());

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
            evidence,
            new FakeCapabilityApprovalStore());

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
            evidence,
            new FakeCapabilityApprovalStore());

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
            evidence,
            new FakeCapabilityApprovalStore());

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

    [Fact]
    public async Task Restore_is_blocked_without_exact_explicit_approval()
    {
        FakeDotNetToolRunner runner = new();
        FakeToolEvidenceStore evidence = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileEditor(),
            runner,
            evidence,
            new FakeCapabilityApprovalStore());

        DotNetOperationView result = await service.RunDotNetAsync(new(
            "goal-id",
            new("restore-call"),
            DotNetOperation.Restore));

        Assert.Equal("restore_not_approved", result.ErrorCode);
        Assert.Equal(0, runner.CallCount);
        Assert.Empty(evidence.Items);
    }

    [Fact]
    public async Task Exact_restore_approval_runs_once_through_the_evidence_boundary()
    {
        FakeDotNetToolRunner runner = new();
        FakeToolEvidenceStore evidence = new();
        FakeCapabilityApprovalStore approvals = new(CreateRestoreApproval(
            "restore-call",
            "Repository.slnx"));
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileEditor(),
            runner,
            evidence,
            approvals);

        DotNetOperationView result = await service.RunDotNetAsync(new(
            "goal-id",
            new("restore-call"),
            DotNetOperation.Restore));
        DotNetOperationView replay = await service.RunDotNetAsync(new(
            "goal-id",
            new("restore-call"),
            DotNetOperation.Restore));

        Assert.Null(result.Error);
        Assert.Equal(DotNetToolOperation.Restore, runner.Request?.Operation);
        Assert.Equal(ToolKind.Restore, Assert.Single(evidence.Items).Tool);
        Assert.Equal(ToolCallState.Succeeded, evidence.Items[0].State);
        Assert.Equal("duplicate_correlation", replay.ErrorCode);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task Restore_approval_for_another_target_does_not_grant_execution()
    {
        FakeDotNetToolRunner runner = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileEditor(),
            runner,
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(CreateRestoreApproval(
                "restore-call",
                "Other.slnx")));

        DotNetOperationView result = await service.RunDotNetAsync(new(
            "goal-id",
            new("restore-call"),
            DotNetOperation.Restore));

        Assert.Equal("restore_not_approved", result.ErrorCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task Denied_restore_approval_does_not_grant_execution()
    {
        FakeDotNetToolRunner runner = new();
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileEditor(),
            runner,
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(CreateRestoreApproval(
                "restore-call",
                "Repository.slnx",
                CapabilityApprovalState.Denied)));

        DotNetOperationView result = await service.RunDotNetAsync(new(
            "goal-id",
            new("restore-call"),
            DotNetOperation.Restore));

        Assert.Equal("restore_not_approved", result.ErrorCode);
        Assert.Equal(0, runner.CallCount);
    }

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

        public ValueTask<WorkspaceFileBatchEditResult> ApplyBatchAsync(
            string worktreeRoot,
            WorkspaceFileBatchEdit batch,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            Root = worktreeRoot;
            return ValueTask.FromResult(new WorkspaceFileBatchEditResult(
                batch.Edits.Select(edit => new WorkspaceFileEditResult(
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
                new(
                    request.Snapshot.Path,
                    request.Snapshot.BaselineHash,
                    request.Snapshot.Text,
                    new("class First { }"),
                    1),
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
