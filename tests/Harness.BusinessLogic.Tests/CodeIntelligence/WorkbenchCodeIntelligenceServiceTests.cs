using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.Tests.CodeIntelligence;

public sealed partial class WorkbenchCodeIntelligenceServiceTests
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
    public async Task Test_discovery_maps_only_exact_confined_session_results()
    {
        CodeIntelligenceTestFramework? requestedFramework = null;
        DeterministicCodeIntelligenceEngine engine = new()
        {
            TestDiscovery = (request, _) =>
            {
                requestedFramework = request.Framework;
                return ValueTask.FromResult<CodeIntelligenceTestDiscoveryResult>(new(
                    request.ContextId,
                    request.SessionId,
                    CodeIntelligenceResultState.Ready,
                    [new(
                        new("test-id"),
                        new("tests/App.Tests/App.Tests.csproj"),
                        CodeIntelligenceTestFramework.XUnit,
                        new("App.Tests.WidgetTests.Works"),
                        new("Works"),
                        new("tests/App.Tests/WidgetTests.cs"),
                        new(new(10, 4), new(10, 9)),
                        [new(new("Category"), new("Fast"))],
                        IsParameterized: false)],
                    Continuation: null,
                    IsTruncated: false,
                    []));
            },
        };
        WorkbenchCodeIntelligenceService service = new(
            new ContextResolver(OriginalResolution()),
            engine);
        WorkbenchCodeSessionId sessionId = (await service.StartAsync(new(
            new("workspace-id"), null, new("Harness.slnx")))).SessionId!;

        WorkbenchCodeTestDiscoveryView result = await service.DiscoverTestsAsync(new(
            sessionId, "Fast", MaximumResults: 100, Offset: 0,
            WorkbenchCodeTestFramework.XUnit));

        WorkbenchCodeTestCase test = Assert.Single(result.Tests);
        Assert.Equal(WorkbenchCodeTestFramework.XUnit, test.Framework);
        Assert.Equal("tests/App.Tests/App.Tests.csproj", test.ProjectPath.Value);
        Assert.Equal("tests/App.Tests/WidgetTests.cs", test.Path.Value);
        Assert.Equal("Fast", Assert.Single(test.Traits).Value.Value);
        Assert.Equal(CodeIntelligenceTestFramework.XUnit, requestedFramework);
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

}
