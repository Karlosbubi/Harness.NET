using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.Tests.CodeIntelligence;

public sealed partial class WorkbenchCodeIntelligenceServiceTests
{
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
                    CodeIntelligenceVirtualDocumentKind.DecompiledSource,
                    new("String · decompiled"), new(virtualText),
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
        Assert.Equal(WorkbenchCodeVirtualDocumentKind.DecompiledSource, document.Kind);
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

}
