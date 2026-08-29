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
    public async Task Cross_document_transformation_applies_one_batch_and_validates_every_file()
    {
        FakeFileEditor editor = new();
        FakeCodeIntelligenceService codeIntelligence = new()
        {
            DocumentTransformationFingerprint = Baseline,
            DocumentTransformationEdits =
            [
                new(new("src/First.cs"), new(Baseline), new("class First { }"),
                    new("class First { void Run(int value) { } }"), 1),
                new(new("tests/Use.cs"), new(Baseline), new("class Use { }"),
                    new("class Use { void Go() { new First().Run(1); } }"), 1),
            ],
        };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        DocumentTransformationApplyView result =
            await service.ApplyDocumentTransformationAsync(new(
                DocumentTransformationRequest(DocumentTransformationOrigin.Human, []),
                new("cross-document-apply"),
                new(Baseline)));

        Assert.Null(result.ErrorCode);
        Assert.Equal(1, editor.BatchCallCount);
        Assert.Equal(2, editor.LastBatch?.Edits.Count);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(2, codeIntelligence.LastValidationRequest?.Edits.Count);
        Assert.Equal(
            ["src/First.cs", "tests/Use.cs"],
            codeIntelligence.LastValidationRequest!.Edits.Select(edit => edit.Path.Value).ToArray());
    }

    [Fact]
    public async Task Model_cross_document_transformation_checks_every_affected_file_grant()
    {
        FakeFileEditor editor = new();
        FakeCodeIntelligenceService codeIntelligence = new()
        {
            DocumentTransformationEdits =
            [
                new(new("src/First.cs"), new(Baseline), new("class First { }"),
                    new("class First { void Run() { } }"), 1),
                new(new("tests/Use.cs"), new(Baseline), new("class Use { }"),
                    new("class Use { First value = new(); }"), 1),
            ],
        };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        DocumentTransformationApplyView result =
            await service.ApplyDocumentTransformationAsync(new(
                DocumentTransformationRequest(
                    DocumentTransformationOrigin.Model, [new("src")]),
                new("cross-document-denied"),
                new(Baseline)));

        Assert.Equal("task_file_area_denied", result.ErrorCode);
        Assert.Equal(0, editor.BatchCallCount);
    }

    [Fact]
    public async Task Incomplete_atomic_transformation_result_fails_closed_before_post_validation()
    {
        FakeFileEditor editor = new() { OmitLastBatchResult = true };
        FakeCodeIntelligenceService codeIntelligence = new()
        {
            DocumentTransformationEdits =
            [
                new(new("src/First.cs"), new(Baseline), new("class First { }"),
                    new("class First { void Run() { } }"), 1),
                new(new("src/Second.cs"), new(Baseline), new("class Second { }"),
                    new("class Second { void Run() { } }"), 1),
            ],
        };
        WorkspaceMutationService service = new(
            new FakeGoalStore(CreateGoal("Approved"), CreateWorktree()),
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            editor,
            new FakeDotNetToolRunner(),
            new FakeToolEvidenceStore(),
            new FakeCapabilityApprovalStore(),
            codeIntelligence);

        DocumentTransformationApplyView result =
            await service.ApplyDocumentTransformationAsync(new(
                DocumentTransformationRequest(DocumentTransformationOrigin.Human, []),
                new("incomplete-cross-document-apply"),
                new(Baseline)));

        Assert.Equal("incomplete_atomic_apply", result.ErrorCode);
        Assert.Null(result.AppliedCodeValidation);
        Assert.Null(codeIntelligence.LastValidationRequest);
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

}
