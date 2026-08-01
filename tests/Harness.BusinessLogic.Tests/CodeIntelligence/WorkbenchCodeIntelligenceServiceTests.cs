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
            ValueTask<CodeIntelligenceDiagnosticResult>>? Diagnostics { get; init; }
        internal Func<CodeIntelligenceCompletionRequest, CancellationToken,
            ValueTask<CodeIntelligenceCompletionResult>>? Completions { get; init; }
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
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceNavigationResult> FindReferencesAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask CloseAsync(
            CodeIntelligenceSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            ClosedSession = sessionId;
            return ValueTask.CompletedTask;
        }
    }
}
