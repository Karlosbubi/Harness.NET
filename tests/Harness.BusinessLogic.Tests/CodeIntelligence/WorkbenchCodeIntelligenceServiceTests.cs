using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.Tests.CodeIntelligence;

public sealed class WorkbenchCodeIntelligenceServiceTests
{
    private const string Baseline =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Trusted_goal_context_is_mapped_to_an_approved_worktree_session()
    {
        ContextResolver resolver = new(ApprovedResolution());
        DeterministicCodeIntelligenceEngine engine = new();
        WorkbenchCodeIntelligenceService service = new(resolver, engine);

        WorkbenchCodeSessionView result = await service.StartAsync(new(
            new("workspace-id"),
            new("goal-id"),
            new("Harness.slnx")));

        Assert.Equal(WorkbenchCodeResultState.Ready, result.State);
        Assert.Equal("session-1", result.SessionId?.Value);
        Assert.Equal(CodeIntelligenceSourceKind.ApprovedGoalWorktree,
            engine.OpenRequest?.SourceKind);
        Assert.Equal("/state/worktrees/goal-id", engine.OpenRequest?.RootPath.Value);
        Assert.Equal("Harness.slnx", engine.OpenRequest?.EntryPoint.Value);
        Assert.NotNull(result.ContextId);
        Assert.Equal(result.ContextId?.Value, engine.OpenRequest?.ContextId.Value);
    }

    [Fact]
    public async Task Revoked_trust_fails_before_the_engine_is_called()
    {
        ContextResolver resolver = new(new(
            new(
                new("workspace-id"),
                null,
                null,
                WorkbenchWorkspaceScope.Unavailable,
                "Workspace context unavailable"),
            RootPath: null,
            "workspace_not_trusted",
            "Trust the workspace before inspecting its content."));
        DeterministicCodeIntelligenceEngine engine = new();
        WorkbenchCodeIntelligenceService service = new(resolver, engine);

        WorkbenchCodeSessionView result = await service.StartAsync(new(
            new("workspace-id"),
            null,
            new("Harness.slnx")));

        Assert.Equal(WorkbenchCodeResultState.Failed, result.State);
        Assert.Equal("workspace_not_trusted", Assert.Single(result.Issues).Code.Value);
        Assert.Equal(0, engine.OpenCallCount);
    }

    [Theory]
    [InlineData("../outside.slnx")]
    [InlineData("src/../outside.slnx")]
    [InlineData("/outside.slnx")]
    [InlineData("notes.md")]
    public async Task Invalid_entry_point_is_rejected_before_context_resolution(string entryPoint)
    {
        ContextResolver resolver = new(ApprovedResolution());
        DeterministicCodeIntelligenceEngine engine = new();
        WorkbenchCodeIntelligenceService service = new(resolver, engine);

        WorkbenchCodeSessionView result = await service.StartAsync(new(
            new("workspace-id"),
            new("goal-id"),
            new(entryPoint)));

        Assert.Equal("invalid_request", Assert.Single(result.Issues).Code.Value);
        Assert.Equal(0, resolver.CallCount);
        Assert.Equal(0, engine.OpenCallCount);
    }

    [Fact]
    public async Task Newer_buffer_supersedes_an_in_flight_diagnostic_result()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DeterministicCodeIntelligenceEngine engine = new()
        {
            Diagnostics = async (snapshot, cancellationToken) =>
            {
                if (snapshot.BufferVersion.Value == 1)
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }

                return ReadyDiagnostics(snapshot);
            },
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()),
            engine);
        WorkbenchCodeSessionView session = await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")));
        WorkbenchCodeSessionId sessionId = session.SessionId!;

        ValueTask<WorkbenchCodeDiagnosticView> first = service.SynchronizeAsync(
            Snapshot(sessionId, version: 1, "class C { }"));
        await firstStarted.Task;
        WorkbenchCodeDiagnosticView second = await service.SynchronizeAsync(
            Snapshot(sessionId, version: 2, "class C { int Value; }"));
        releaseFirst.SetResult();
        WorkbenchCodeDiagnosticView stale = await first;

        Assert.Equal(WorkbenchCodeResultState.Ready, second.State);
        Assert.Equal(WorkbenchCodeResultState.Stale, stale.State);
        Assert.Equal("stale_buffer", Assert.Single(stale.Issues).Code.Value);
    }

    [Fact]
    public async Task Cancellation_is_a_closed_diagnostic_state()
    {
        DeterministicCodeIntelligenceEngine engine = new()
        {
            Diagnostics = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            },
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()),
            engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        WorkbenchCodeDiagnosticView result = await service.SynchronizeAsync(
            Snapshot(sessionId, version: 1, "class C { }"),
            cancellation.Token);

        Assert.Equal(WorkbenchCodeResultState.Cancelled, result.State);
        Assert.Equal("cancelled", Assert.Single(result.Issues).Code.Value);
    }

    [Fact]
    public async Task Candidate_validation_requires_an_approved_worktree_and_confined_paths()
    {
        DeterministicCodeIntelligenceEngine engine = new();
        WorkbenchCodeIntelligenceService original = new(
            new ContextResolver(OriginalResolution()),
            engine);
        WorkbenchCodeSessionId originalSession = (await original.StartAsync(new(
            new("workspace-id"), null, new("Harness.slnx")))).SessionId!;

        WorkbenchCodeValidationView readOnly = await original.ValidateAsync(new(
            originalSession,
            WorkbenchCodeValidationPhase.Candidate,
            [new(new("src/App.cs"), new(Baseline), new("class App { }"))]));

        Assert.Equal("editable_context_required", Assert.Single(readOnly.Issues).Code.Value);
        Assert.Equal(0, engine.ValidateCallCount);

        WorkbenchCodeIntelligenceService approved = new(
            new ContextResolver(ApprovedResolution()),
            engine);
        WorkbenchCodeSessionId approvedSession = (await approved.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeValidationView escaped = await approved.ValidateAsync(new(
            approvedSession,
            WorkbenchCodeValidationPhase.Candidate,
            [new(new("../App.cs"), new(Baseline), new("class App { }"))]));

        Assert.Equal("invalid_candidate", Assert.Single(escaped.Issues).Code.Value);
        Assert.Equal(0, engine.ValidateCallCount);
    }

    [Fact]
    public async Task Applied_validation_phase_crosses_the_boundary_explicitly()
    {
        DeterministicCodeIntelligenceEngine engine = new();
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()),
            engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;

        WorkbenchCodeValidationView result = await service.ValidateAsync(new(
            sessionId,
            WorkbenchCodeValidationPhase.Applied,
            [new(new("src/App.cs"), new(Baseline), new("class App { }"))]));

        Assert.Equal(WorkbenchCodeValidationDisposition.Validated, result.Disposition);
        Assert.Equal(CodeIntelligenceValidationPhase.Applied, engine.LastValidation?.Phase);
    }

    [Fact]
    public async Task Newer_buffer_discards_an_in_flight_completion_result()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DeterministicCodeIntelligenceEngine engine = new()
        {
            Completions = async (request, cancellationToken) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new(
                    request.Snapshot.ContextId,
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready,
                    new("list-1"),
                    new(request.Snapshot.Position, request.Snapshot.Position),
                    [],
                    []);
            },
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        _ = await service.SynchronizeAsync(Snapshot(sessionId, version: 1, "class C { }"));
        WorkbenchCodeInteractiveSnapshot interactive = new(
            sessionId,
            new("src/App.cs"),
            new(Baseline),
            new(1),
            new("class C { }"),
            new(0, 5));

        Task<WorkbenchCodeCompletionView> pending = service.GetCompletionsAsync(new(
            interactive,
            WorkbenchCodeCompletionTriggerKind.Invoke,
            TriggerCharacter: null)).AsTask();
        await entered.Task;
        _ = await service.SynchronizeAsync(Snapshot(sessionId, version: 2, "class C { int X; }"));
        release.SetResult();
        WorkbenchCodeCompletionView result = await pending;

        Assert.Equal(WorkbenchCodeResultState.Stale, result.State);
        Assert.Equal("stale_buffer", Assert.Single(result.Issues).Code.Value);
    }

    [Fact]
    public async Task Newer_buffer_discards_in_flight_semantic_presentation()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DeterministicCodeIntelligenceEngine engine = new()
        {
            Presentations = async (request, cancellationToken) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new(
                    request.Snapshot.ContextId,
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready,
                    [new(new(new(0, 0), new(0, 5)),
                        CodeIntelligenceClassificationKind.Keyword)],
                    [], [], [], [], [], false, []);
            },
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        _ = await service.SynchronizeAsync(Snapshot(sessionId, version: 1, "class C { }"));
        WorkbenchCodeInteractiveSnapshot interactive = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new("class C { }"), new(0, 6));

        Task<WorkbenchCodeDocumentPresentationView> pending = service
            .GetDocumentPresentationAsync(new(interactive, VisibleRange: null)).AsTask();
        await entered.Task;
        _ = await service.SynchronizeAsync(Snapshot(
            sessionId, version: 2, "class C { int X; }"));
        release.SetResult();
        WorkbenchCodeDocumentPresentationView result = await pending;

        Assert.Equal(WorkbenchCodeResultState.Stale, result.State);
        Assert.Empty(result.Classifications);
        Assert.Equal("stale_buffer", Assert.Single(result.Issues).Code.Value);
    }

    [Fact]
    public async Task Typed_execution_target_crosses_the_code_intelligence_boundary()
    {
        DeterministicCodeIntelligenceEngine engine = new()
        {
            Presentations = (request, _) => ValueTask.FromResult(new
                CodeIntelligenceDocumentPresentationResult(
                    request.Snapshot.ContextId, request.Snapshot.SessionId,
                    request.Snapshot.Path, request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready, [], [], [], [], [],
                    [new(new(0, 0), new(0, 20), CodeIntelligenceCodeLensKind.Run,
                        new("Run project"), true,
                        new(CodeIntelligenceExecutionTargetKind.ProjectEntryPoint,
                            new("src/App/App.csproj"), new("net10.0"),
                            new("M:Program.Main"), request.Snapshot.Path,
                            request.Snapshot.BaselineHash, request.Snapshot.BufferVersion))],
                    false, [])),
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new("class Program { static void Main() { } }"), new(0, 28));

        WorkbenchCodeDocumentPresentationView result =
            await service.GetDocumentPresentationAsync(new(snapshot, null,
                CodeLens: new(false, false, false, true, false)));

        WorkbenchCodeLens lens = Assert.Single(result.CodeLenses);
        Assert.Equal(WorkbenchCodeLensKind.Run, lens.Kind);
        Assert.Equal("src/App/App.csproj", lens.ExecutionTarget?.ProjectPath.Value);
        Assert.Equal("M:Program.Main", lens.ExecutionTarget?.DeclarationId.Value);
        Assert.Equal(Baseline, lens.ExecutionTarget?.SourceBaseline.Value);
    }

    [Fact]
    public async Task Newer_buffer_discards_an_in_flight_rename_preview()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DeterministicCodeIntelligenceEngine engine = new()
        {
            Renames = async (request, cancellationToken) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new(
                    request.Snapshot.ContextId,
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready,
                    CodeIntelligenceTransformationDisposition.Ready,
                    new("Class|C"),
                    request.NewName,
                    [new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                        request.Snapshot.Text,
                        new("class Renamed { }"), 1)],
                    [],
                    [],
                    new(Baseline),
                    []);
            },
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        _ = await service.SynchronizeAsync(Snapshot(sessionId, 1, "class C { }"));
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new("class C { }"), new(0, 6));

        Task<WorkbenchCodeRenamePreviewView> pending = service.PreviewRenameAsync(new(
            snapshot, new("Renamed"))).AsTask();
        await entered.Task;
        _ = await service.SynchronizeAsync(Snapshot(sessionId, 2, "class C { int X; }"));
        release.SetResult();
        WorkbenchCodeRenamePreviewView result = await pending;

        Assert.Equal(WorkbenchCodeResultState.Stale, result.State);
        Assert.Equal("stale_buffer", Assert.Single(result.Issues).Code.Value);
        Assert.Null(result.Fingerprint);
    }

    [Fact]
    public async Task Document_transformation_maps_exact_edit_and_fingerprint()
    {
        const string source = "class C{ }";
        const string formatted = "class C { }";
        DeterministicCodeIntelligenceEngine engine = new()
        {
            DocumentTransformations = (request, _) =>
                ValueTask.FromResult<CodeIntelligenceDocumentTransformationPreviewResult>(new(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                CodeIntelligenceResultState.Ready,
                CodeIntelligenceTransformationDisposition.Ready,
                request.Kind,
                request.Range,
                [new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                    request.Snapshot.Text, new(formatted), 1)],
                [],
                [],
                new(Baseline),
                [])),
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new(source), new(0, 0));

        WorkbenchCodeDocumentTransformationPreviewView result =
            await service.PreviewDocumentTransformationAsync(new(
                snapshot, WorkbenchCodeDocumentTransformationKind.FormatDocument, Range: null));

        Assert.Equal(WorkbenchCodeTransformationDisposition.Ready, result.Disposition);
        Assert.Equal(formatted, result.Edit!.Text.Value);
        Assert.Equal(Baseline, result.Fingerprint!.Value);
        Assert.Equal(WorkbenchCodeDocumentTransformationKind.FormatDocument, result.Kind);
    }

    [Fact]
    public async Task Cross_document_transformation_maps_all_bounded_edits()
    {
        const string source = "class C { int Value { get; set; } }";
        DeterministicCodeIntelligenceEngine engine = new()
        {
            DocumentTransformations = (request, _) => ValueTask.FromResult(
                new CodeIntelligenceDocumentTransformationPreviewResult(
                    request.Snapshot.ContextId,
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready,
                    CodeIntelligenceTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [
                        new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                            request.Snapshot.Text, new("class C { int GetValue() => 0; }"), 1),
                        new(new("src/Use.cs"), request.Snapshot.BaselineHash,
                            new("class Use { int Read(C value) => value.Value; }"),
                            new("class Use { int Read(C value) => value.GetValue(); }"), 1),
                    ],
                    [],
                    [],
                    new(Baseline),
                    [],
                    CodeActionId: request.CodeActionId,
                    CodeActionScope: request.CodeActionScope)),
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new(source), new(0, 14));

        WorkbenchCodeDocumentTransformationPreviewView result =
            await service.PreviewDocumentTransformationAsync(new(
                snapshot,
                WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                Range: null,
                CodeActionId: new(Baseline),
                CodeActionScope: WorkbenchCodeActionScope.Occurrence));

        Assert.Equal(WorkbenchCodeTransformationDisposition.Ready, result.Disposition);
        Assert.Equal(["src/App.cs", "src/Use.cs"],
            result.Edits.Select(edit => edit.Path.Value).ToArray());
        Assert.Null(result.Edit);
    }

    [Fact]
    public async Task Triggered_formatting_preserves_the_typed_trigger_across_the_boundary()
    {
        CodeIntelligenceFormattingTrigger? observed = null;
        DeterministicCodeIntelligenceEngine engine = new()
        {
            DocumentTransformations = (request, _) =>
            {
                observed = request.FormattingTrigger;
                return ValueTask.FromResult(new CodeIntelligenceDocumentTransformationPreviewResult(
                    request.Snapshot.ContextId,
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready,
                    CodeIntelligenceTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                        request.Snapshot.Text, new("class C { int Value = 1; }"), 1)],
                    [],
                    [],
                    new(Baseline),
                    [],
                    ImportNamespace: null,
                    FormattingTrigger: request.FormattingTrigger));
            },
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new("class C{int Value=1;}"), new(0, 21));

        WorkbenchCodeDocumentTransformationPreviewView result =
            await service.PreviewDocumentTransformationAsync(new(
                snapshot,
                WorkbenchCodeDocumentTransformationKind.FormatOnType,
                new(new(0, 20), new(0, 21)),
                ImportNamespace: null,
                FormattingTrigger: WorkbenchCodeFormattingTrigger.Semicolon));

        Assert.Equal(CodeIntelligenceFormattingTrigger.Semicolon, observed);
        Assert.Equal(WorkbenchCodeFormattingTrigger.Semicolon, result.FormattingTrigger);
        Assert.Equal(WorkbenchCodeTransformationDisposition.Ready, result.Disposition);
    }

    [Fact]
    public async Task Missing_import_discovery_maps_typed_candidates()
    {
        DeterministicCodeIntelligenceEngine engine = new()
        {
            MissingImports = (snapshot, _) => ValueTask.FromResult(new
                CodeIntelligenceMissingImportResult(
                    snapshot.ContextId,
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready,
                    [new(new("System.Text"), new("System.Text.StringBuilder"),
                        new(new(0, 10), new(0, 23)))],
                    [])),
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new("class C { StringBuilder Value; }"), new(0, 13));

        WorkbenchCodeMissingImportView result = await service.GetMissingImportsAsync(snapshot);

        WorkbenchCodeMissingImportCandidate candidate = Assert.Single(result.Candidates);
        Assert.Equal("System.Text", candidate.Namespace.Value);
        Assert.Equal("System.Text.StringBuilder", candidate.Symbol.Value);
    }

    [Fact]
    public async Task Closed_code_action_identity_and_scope_cross_the_boundary_exactly()
    {
        const string source = "interface I { void Run(); } class C : I { }";
        CodeIntelligenceCodeActionId actionId = new(Baseline);
        DeterministicCodeIntelligenceEngine engine = new()
        {
            CodeActions = (snapshot, _) => ValueTask.FromResult(new
                CodeIntelligenceCodeActionResult(
                    snapshot.ContextId, snapshot.SessionId, snapshot.Path,
                    snapshot.BufferVersion, CodeIntelligenceResultState.Ready,
                    [new(actionId, CodeIntelligenceClosedCodeActionKind.ImplementInterface,
                        CodeIntelligenceCodeActionScope.Occurrence,
                        new("Implement interface"), new("CS0535"),
                        new(new(0, 39), new(0, 40)),
                        AffectedFileCount: 2,
                        ChangesActiveDocument: false)], [])),
            DocumentTransformations = (request, _) => ValueTask.FromResult(new
                CodeIntelligenceDocumentTransformationPreviewResult(
                    request.Snapshot.ContextId, request.Snapshot.SessionId,
                    request.Snapshot.Path, request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready,
                    CodeIntelligenceTransformationDisposition.Ready, request.Kind,
                    request.Range,
                    [new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                        request.Snapshot.Text, new(source + " void Run() { }"), 1)],
                    [], [], new(Baseline), [],
                    CodeActionId: request.CodeActionId,
                    CodeActionScope: request.CodeActionScope)),
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new(source), new(0, 39));

        WorkbenchCodeActionCandidate candidate = Assert.Single(
            (await service.GetCodeActionsAsync(new(snapshot))).Candidates);
        WorkbenchCodeDocumentTransformationPreviewView preview =
            await service.PreviewDocumentTransformationAsync(new(
                snapshot, WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                Range: null, CodeActionId: candidate.Id,
                CodeActionScope: candidate.Scope));

        Assert.Equal(WorkbenchClosedCodeActionKind.ImplementInterface, candidate.Kind);
        Assert.Equal(Baseline, candidate.Id.Value);
        Assert.Equal(WorkbenchCodeActionScope.Occurrence, candidate.Scope);
        Assert.Equal(2, candidate.AffectedFileCount);
        Assert.False(candidate.ChangesActiveDocument);
        Assert.Equal(candidate.Id, preview.CodeActionId);
        Assert.Equal(candidate.Scope, preview.CodeActionScope);
        Assert.Equal(WorkbenchCodeTransformationDisposition.Ready, preview.Disposition);
    }

    [Fact]
    public async Task Add_missing_import_preserves_the_exact_namespace_across_the_boundary()
    {
        CodeIntelligenceImportNamespace? observed = null;
        DeterministicCodeIntelligenceEngine engine = new()
        {
            DocumentTransformations = (request, _) =>
            {
                observed = request.ImportNamespace;
                return ValueTask.FromResult(new CodeIntelligenceDocumentTransformationPreviewResult(
                    request.Snapshot.ContextId,
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready,
                    CodeIntelligenceTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                        request.Snapshot.Text, new("using System.Text;\nclass C { StringBuilder Value; }"), 1)],
                    [],
                    [],
                    new(Baseline),
                    [],
                    request.ImportNamespace));
            },
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new("class C { StringBuilder Value; }"), new(0, 13));

        WorkbenchCodeDocumentTransformationPreviewView result =
            await service.PreviewDocumentTransformationAsync(new(
                snapshot,
                WorkbenchCodeDocumentTransformationKind.AddMissingImport,
                Range: null,
                new("System.Text")));

        Assert.Equal("System.Text", observed?.Value);
        Assert.Equal("System.Text", result.ImportNamespace?.Value);
        Assert.Equal(WorkbenchCodeTransformationDisposition.Ready, result.Disposition);
    }

    [Fact]
    public async Task Stop_invalidates_the_session_and_disposes_the_engine_state()
    {
        DeterministicCodeIntelligenceEngine engine = new();
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()),
            engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;

        await service.StopAsync(sessionId);
        WorkbenchCodeDiagnosticView result = await service.SynchronizeAsync(
            Snapshot(sessionId, version: 1, "class C { }"));

        Assert.Equal("session_unavailable", Assert.Single(result.Issues).Code.Value);
        Assert.Equal(sessionId.Value, engine.ClosedSession?.Value);
    }

    [Fact]
    public async Task Virtual_document_identity_and_origin_cross_the_business_boundary()
    {
        const string source = "class C { string Value = string.Empty; }";
        const string virtualText = "public sealed class String { public static string Empty; }";
        string id = new('a', 64);
        DeterministicCodeIntelligenceEngine engine = new()
        {
            Navigation = (snapshot, _) => ValueTask.FromResult(new CodeIntelligenceNavigationResult(
                snapshot.ContextId, snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                CodeIntelligenceResultState.Ready,
                [new(CodeIntelligenceDestinationKind.Metadata, new("string.Empty"), null, null,
                    new(id))], [])),
            VirtualDocuments = (request, _) => ValueTask.FromResult(
                new CodeIntelligenceVirtualDocumentResult(
                    request.Snapshot.ContextId, request.Snapshot.SessionId,
                    request.Snapshot.Path, request.Snapshot.BufferVersion,
                    CodeIntelligenceResultState.Ready, request.Id,
                    CodeIntelligenceVirtualDocumentKind.MetadataSignature,
                    new("String · metadata"), new(virtualText),
                    new(new(0, 20), new(0, 26)),
                    new(new("Sample"), new("project-version"), new("net10.0"),
                        new("Debug"), new("System.Runtime, Version=10.0.0.0"),
                        new(new string('b', 64))),
                    IsReadOnly: true, [])),
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1), new(source), new(0, 38));

        WorkbenchCodeNavigationView navigation = await service.FindDefinitionAsync(snapshot);
        WorkbenchCodeSymbolDestination destination = Assert.Single(navigation.Destinations);
        WorkbenchCodeVirtualDocumentView document = await service.GetVirtualDocumentAsync(
            new(snapshot, destination.VirtualDocumentId!));

        Assert.Equal(id, destination.VirtualDocumentId!.Value);
        Assert.Equal(virtualText, document.Text!.Value);
        Assert.True(document.IsReadOnly);
        Assert.Equal("net10.0", document.Origin!.TargetFramework.Value);
        Assert.Equal(new string('b', 64), document.Origin.Compilation.Value);
    }

    [Fact]
    public async Task Inspection_kind_text_and_exact_origin_cross_the_business_boundary()
    {
        DeterministicCodeIntelligenceEngine engine = new()
        {
            Inspections = (request, _) => ValueTask.FromResult(new CodeIntelligenceInspectionResult(
                request.Snapshot.ContextId, request.Snapshot.SessionId, request.Snapshot.Path,
                request.Snapshot.BufferVersion, CodeIntelligenceResultState.Ready, request.Kind,
                new("IL · Run"), new("IL_0000: ret"),
                new(new("Sample"), new("project-version"), new("net10.0"), new("Release"),
                    new("Sample, Version=1.0.0.0"), new(new string('c', 64))),
                IsReadOnly: true, IsTruncated: false, [])),
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(ApprovedResolution()), engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), new("goal-id"), new("Harness.slnx")))).SessionId!;
        WorkbenchCodeInteractiveSnapshot snapshot = new(
            sessionId, new("src/App.cs"), new(Baseline), new(1),
            new("class C { void Run() { } }"), new(0, 18));

        WorkbenchCodeInspectionView result = await service.InspectAsync(new(
            snapshot, WorkbenchCodeInspectionKind.IntermediateLanguage));

        Assert.Equal(WorkbenchCodeInspectionKind.IntermediateLanguage, result.Kind);
        Assert.Equal("IL_0000: ret", result.Text!.Value);
        Assert.True(result.IsReadOnly);
        Assert.Equal("Release", result.Origin!.Configuration.Value);
        Assert.Equal(new string('c', 64), result.Origin.Compilation.Value);
    }

    private static WorkbenchCodeDocumentSnapshot Snapshot(
        WorkbenchCodeSessionId sessionId,
        long version,
        string text) => new(
        sessionId,
        new("src/App.cs"),
        new(Baseline),
        new(version),
        new(text));

    private static CodeIntelligenceDiagnosticResult ReadyDiagnostics(
        CodeIntelligenceDocumentSnapshot snapshot) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        CodeIntelligenceResultState.Ready,
        [],
        []);

    private static WorkbenchWorkspaceResolution ApprovedResolution() => new(
        new(
            new("workspace-id"),
            new GoalId("goal-id"),
            new("harness/goal-test"),
            WorkbenchWorkspaceScope.ApprovedGoalWorktree,
            "Approved goal worktree"),
        "/state/worktrees/goal-id",
        ErrorCode: null,
        Error: null);

    private static WorkbenchWorkspaceResolution OriginalResolution() => new(
        new(
            new("workspace-id"),
            null,
            new("main"),
            WorkbenchWorkspaceScope.OriginalWorkspace,
            "Original workspace"),
        "/workspace/repository",
        ErrorCode: null,
        Error: null);

    private sealed class ContextResolver(WorkbenchWorkspaceResolution result)
        : IWorkbenchWorkspaceContextResolver
    {
        internal int CallCount { get; private set; }

        public ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DeterministicCodeIntelligenceEngine : ICodeIntelligenceEngine
    {
        internal Func<CodeIntelligenceDocumentSnapshot, CancellationToken,
            ValueTask<CodeIntelligenceDiagnosticResult>>? Diagnostics
        { get; init; }
        internal Func<CodeIntelligenceCompletionRequest, CancellationToken,
            ValueTask<CodeIntelligenceCompletionResult>>? Completions
        { get; init; }
        internal Func<CodeIntelligenceRenamePreviewRequest, CancellationToken,
            ValueTask<CodeIntelligenceRenamePreviewResult>>? Renames
        { get; init; }
        internal Func<CodeIntelligenceDocumentTransformationPreviewRequest, CancellationToken,
            ValueTask<CodeIntelligenceDocumentTransformationPreviewResult>>? DocumentTransformations
        { get; init; }
        internal Func<CodeIntelligenceInteractiveSnapshot, CancellationToken,
            ValueTask<CodeIntelligenceMissingImportResult>>? MissingImports
        { get; init; }
        internal Func<CodeIntelligenceInteractiveSnapshot, CancellationToken,
            ValueTask<CodeIntelligenceCodeActionResult>>? CodeActions
        { get; init; }
        internal Func<CodeIntelligenceDocumentPresentationRequest, CancellationToken,
            ValueTask<CodeIntelligenceDocumentPresentationResult>>? Presentations
        { get; init; }
        internal Func<CodeIntelligenceInteractiveSnapshot, CancellationToken,
            ValueTask<CodeIntelligenceNavigationResult>>? Navigation
        { get; init; }
        internal Func<CodeIntelligenceVirtualDocumentRequest, CancellationToken,
            ValueTask<CodeIntelligenceVirtualDocumentResult>>? VirtualDocuments
        { get; init; }
        internal Func<CodeIntelligenceInspectionRequest, CancellationToken,
            ValueTask<CodeIntelligenceInspectionResult>>? Inspections
        { get; init; }
        internal CodeIntelligenceOpenRequest? OpenRequest { get; private set; }
        internal CodeIntelligenceSessionId? ClosedSession { get; private set; }
        internal int OpenCallCount { get; private set; }
        internal int ValidateCallCount { get; private set; }
        internal CodeIntelligenceValidationRequest? LastValidation { get; private set; }

        public ValueTask<CodeIntelligenceSessionResult> OpenAsync(
            CodeIntelligenceOpenRequest request,
            IProgress<CodeIntelligenceLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            OpenCallCount++;
            OpenRequest = request;
            return ValueTask.FromResult(new CodeIntelligenceSessionResult(
                request.ContextId,
                new("session-1"),
                CodeIntelligenceResultState.Ready,
                []));
        }

        public ValueTask<CodeIntelligenceDiagnosticResult> GetDiagnosticsAsync(
            CodeIntelligenceDocumentSnapshot snapshot,
            CancellationToken cancellationToken = default) => Diagnostics is null
            ? ValueTask.FromResult(ReadyDiagnostics(snapshot))
            : Diagnostics(snapshot, cancellationToken);

        public ValueTask<CodeIntelligenceValidationResult> ValidateAsync(
            CodeIntelligenceValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateCallCount++;
            LastValidation = request;
            return ValueTask.FromResult(new CodeIntelligenceValidationResult(
                request.ContextId,
                request.SessionId,
                CodeIntelligenceResultState.Ready,
                CodeIntelligenceValidationDisposition.Validated,
                [],
                []));
        }

        public ValueTask<CodeIntelligenceCompletionResult> GetCompletionsAsync(
            CodeIntelligenceCompletionRequest request,
            CancellationToken cancellationToken = default) => Completions is null
            ? throw new NotSupportedException()
            : Completions(request, cancellationToken);
        public ValueTask<CodeIntelligenceCompletionCommitResult> CommitCompletionAsync(
            CodeIntelligenceCompletionCommitRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceQuickInfoResult> GetQuickInfoAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceSignatureHelpResult> GetSignatureHelpAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceNavigationResult> FindDefinitionAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => Navigation is null
            ? throw new NotSupportedException() : Navigation(snapshot, cancellationToken);
        public ValueTask<CodeIntelligenceNavigationResult> FindReferencesAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceVirtualDocumentResult> GetVirtualDocumentAsync(
            CodeIntelligenceVirtualDocumentRequest request,
            CancellationToken cancellationToken = default) => VirtualDocuments is null
            ? throw new NotSupportedException() : VirtualDocuments(request, cancellationToken);
        public ValueTask<CodeIntelligenceInspectionResult> InspectAsync(
            CodeIntelligenceInspectionRequest request,
            CancellationToken cancellationToken = default) => Inspections is null
            ? throw new NotSupportedException() : Inspections(request, cancellationToken);
        public ValueTask<CodeIntelligenceDocumentPresentationResult> GetDocumentPresentationAsync(
            CodeIntelligenceDocumentPresentationRequest request,
            CancellationToken cancellationToken = default) => Presentations is null
            ? throw new NotSupportedException()
            : Presentations(request, cancellationToken);
        public ValueTask<CodeIntelligenceRenamePreviewResult> PreviewRenameAsync(
            CodeIntelligenceRenamePreviewRequest request,
            CancellationToken cancellationToken = default) => Renames is null
            ? throw new NotSupportedException()
            : Renames(request, cancellationToken);
        public ValueTask<CodeIntelligenceDocumentTransformationPreviewResult>
            PreviewDocumentTransformationAsync(
                CodeIntelligenceDocumentTransformationPreviewRequest request,
                CancellationToken cancellationToken = default) =>
            DocumentTransformations is null
                ? throw new NotSupportedException()
                : DocumentTransformations(request, cancellationToken);
        public ValueTask<CodeIntelligenceMissingImportResult> GetMissingImportsAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => MissingImports is null
            ? throw new NotSupportedException()
            : MissingImports(snapshot, cancellationToken);
        public ValueTask<CodeIntelligenceCodeActionResult> GetCodeActionsAsync(
            CodeIntelligenceCodeActionRequest request,
            CancellationToken cancellationToken = default) => CodeActions is null
            ? throw new NotSupportedException()
            : CodeActions(request.Snapshot, cancellationToken);

        public ValueTask CloseAsync(
            CodeIntelligenceSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            ClosedSession = sessionId;
            return ValueTask.CompletedTask;
        }
    }
}
