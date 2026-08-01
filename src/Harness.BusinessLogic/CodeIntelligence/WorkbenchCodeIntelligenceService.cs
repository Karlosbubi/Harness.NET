using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed class WorkbenchCodeIntelligenceService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    ICodeIntelligenceEngine engine) : IWorkbenchCodeIntelligenceService
{
    private const int MaximumCandidateEdits = 100;
    private const int MaximumDiagnostics = 5_000;
    private const int MaximumIssues = 100;
    private const int MaximumIssueMessageLength = 2_048;
    private readonly ConcurrentDictionary<string, ActiveSession> sessions =
        new(StringComparer.Ordinal);

    public async ValueTask<WorkbenchCodeSessionView> StartAsync(
        WorkbenchCodeSessionRequest request,
        IProgress<WorkbenchCodeLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkspaceId is null || string.IsNullOrWhiteSpace(request.WorkspaceId.Value) ||
            request.EntryPoint is null || !IsConfinedRelativePath(request.EntryPoint.Value) ||
            !IsSupportedEntryPoint(request.EntryPoint.Value))
        {
            return SessionFailure("invalid_request",
                "A workspace and confined .slnx, .sln, or project entry point are required.");
        }

        WorkbenchWorkspaceResolution resolution;
        try
        {
            resolution = await contextResolver.ResolveAsync(
                new(request.WorkspaceId, request.GoalId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(
                null,
                null,
                WorkbenchCodeResultState.Cancelled,
                [Issue("cancelled", "Source-context resolution was cancelled.")]);
        }
        if (resolution.RootPath is null || resolution.Error is not null ||
            resolution.Context.Scope is WorkbenchWorkspaceScope.Unavailable)
        {
            return SessionFailure(
                resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The trusted workspace context is unavailable.");
        }

        CodeIntelligenceSourceKind sourceKind = resolution.Context.Scope switch
        {
            WorkbenchWorkspaceScope.ApprovedGoalWorktree =>
                CodeIntelligenceSourceKind.ApprovedGoalWorktree,
            WorkbenchWorkspaceScope.OriginalWorkspace =>
                CodeIntelligenceSourceKind.OriginalWorkspace,
            _ => throw new InvalidOperationException("Unsupported workspace scope."),
        };
        CodeIntelligenceContextId contextId = CreateContextId(
            resolution,
            request.EntryPoint);
        CodeIntelligenceSessionResult result;
        try
        {
            result = await engine.OpenAsync(
                new(
                    contextId,
                    new(resolution.RootPath),
                    new(request.EntryPoint.Value),
                    sourceKind),
                progress is null ? null : new LoadProgressAdapter(progress),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(
                new(contextId.Value),
                null,
                WorkbenchCodeResultState.Cancelled,
                [Issue("cancelled", "Code-intelligence loading was cancelled.")]);
        }

        if (result.ContextId != contextId)
        {
            return new(
                new(contextId.Value),
                null,
                WorkbenchCodeResultState.Failed,
                [Issue("context_mismatch", "The code-intelligence adapter returned a different source context.")]);
        }

        if (result.SessionId is not null)
        {
            sessions[result.SessionId.Value] = new(
                contextId,
                result.SessionId,
                sourceKind);
        }

        return new(
            new(contextId.Value),
            result.SessionId is null ? null : new(result.SessionId.Value),
            Map(result.State),
            MapIssues(result.Issues));
    }

    public async ValueTask<WorkbenchCodeDiagnosticView> SynchronizeAsync(
        WorkbenchCodeDocumentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryValidateSnapshot(snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue))
        {
            return DiagnosticFailure(snapshot, WorkbenchCodeResultState.Failed, issue!);
        }

        ActiveSession activeSession = session!;
        lock (activeSession.Gate)
        {
            if (activeSession.DocumentVersions.TryGetValue(snapshot.Path.Value, out long current) &&
                snapshot.BufferVersion.Value <= current)
            {
                return DiagnosticFailure(
                    snapshot,
                    WorkbenchCodeResultState.Stale,
                    Issue("stale_buffer", "A newer or equal document buffer version is already active."));
            }

            activeSession.DocumentVersions[snapshot.Path.Value] = snapshot.BufferVersion.Value;
        }

        CodeIntelligenceDiagnosticResult result;
        try
        {
            result = await engine.GetDiagnosticsAsync(
                new(
                    activeSession.ContextId,
                    activeSession.SessionId,
                    new(snapshot.Path.Value),
                    new(snapshot.BaselineHash.Value),
                    new(snapshot.BufferVersion.Value),
                    new(snapshot.Text.Value)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DiagnosticFailure(
                snapshot,
                WorkbenchCodeResultState.Cancelled,
                Issue("cancelled", "Document diagnostics were cancelled."));
        }

        lock (activeSession.Gate)
        {
            if (activeSession.DocumentVersions[snapshot.Path.Value] != snapshot.BufferVersion.Value)
            {
                return DiagnosticFailure(
                    snapshot,
                    WorkbenchCodeResultState.Stale,
                    Issue("stale_buffer", "A newer document buffer superseded these diagnostics."));
            }
        }

        if (result.ContextId != activeSession.ContextId ||
            result.SessionId != activeSession.SessionId ||
            result.Path.Value != snapshot.Path.Value ||
            result.BufferVersion.Value != snapshot.BufferVersion.Value)
        {
            return DiagnosticFailure(
                snapshot,
                WorkbenchCodeResultState.Stale,
                Issue("result_identity_mismatch",
                    "Diagnostics did not match the active context, document, and buffer version."));
        }

        return new(
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            Map(result.State),
            result.Diagnostics
                .Where(IsValidDiagnostic)
                .Take(MaximumDiagnostics)
                .Select(Map)
                .ToArray(),
            MapIssues(result.Issues));
    }

    public async ValueTask<WorkbenchCodeValidationView> ValidateAsync(
        WorkbenchCodeValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SessionId is null ||
            !sessions.TryGetValue(request.SessionId.Value, out ActiveSession? session))
        {
            return ValidationFailure(
                request.SessionId ?? new(string.Empty),
                "session_unavailable",
                "The code-intelligence session is unavailable.");
        }

        if (session.SourceKind is not CodeIntelligenceSourceKind.ApprovedGoalWorktree)
        {
            return ValidationFailure(
                request.SessionId,
                "editable_context_required",
                "Candidate validation requires an approved goal worktree context.");
        }

        if (!Enum.IsDefined(request.Phase) || request.Edits is null ||
            request.Edits.Count is 0 or > MaximumCandidateEdits ||
            request.Edits.Any(edit => edit.Path is null ||
                !IsConfinedRelativePath(edit.Path.Value) ||
                edit.BaselineHash is null || !IsSha256(edit.BaselineHash.Value) ||
                edit.Text is null) ||
            request.Edits.Select(edit => edit.Path.Value).Distinct(StringComparer.Ordinal).Count() !=
            request.Edits.Count)
        {
            return ValidationFailure(
                request.SessionId,
                "invalid_candidate",
                "Candidate edits require unique confined paths, exact baselines, and text.");
        }

        CodeIntelligenceValidationResult result;
        try
        {
            result = await engine.ValidateAsync(
                new(
                    session.ContextId,
                    session.SessionId,
                    request.Phase switch
                    {
                        WorkbenchCodeValidationPhase.Candidate =>
                            CodeIntelligenceValidationPhase.Candidate,
                        WorkbenchCodeValidationPhase.Applied =>
                            CodeIntelligenceValidationPhase.Applied,
                        _ => throw new ArgumentOutOfRangeException(nameof(request)),
                    },
                    request.Edits.Select(edit => new CodeIntelligenceCandidateEdit(
                        new(edit.Path.Value),
                        new(edit.BaselineHash.Value),
                        new(edit.Text.Value))).ToArray()),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(
                request.SessionId,
                WorkbenchCodeResultState.Cancelled,
                WorkbenchCodeValidationDisposition.Rejected,
                [],
                [Issue("cancelled", "Candidate validation was cancelled.")]);
        }

        if (result.ContextId != session.ContextId || result.SessionId != session.SessionId)
        {
            return ValidationFailure(
                request.SessionId,
                "result_identity_mismatch",
                "Validation evidence did not match the active source context.");
        }

        return new(
            request.SessionId,
            Map(result.State),
            Map(result.Disposition),
            result.Diagnostics
                .Where(diagnostic => IsValidDiagnostic(diagnostic.Diagnostic))
                .Take(MaximumDiagnostics)
                .Select(diagnostic => new WorkbenchCodeValidationDiagnostic(
                    Map(diagnostic.Kind),
                    Map(diagnostic.Diagnostic)))
                .ToArray(),
            MapIssues(result.Issues));
    }

    public async ValueTask StopAsync(
        WorkbenchCodeSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        if (sessions.TryRemove(sessionId.Value, out ActiveSession? session))
        {
            await engine.CloseAsync(session.SessionId, cancellationToken);
        }
    }

    private bool TryValidateSnapshot(
        WorkbenchCodeDocumentSnapshot snapshot,
        out ActiveSession? session,
        out WorkbenchCodeIssue? issue)
    {
        session = null;
        if (snapshot.SessionId is null ||
            !sessions.TryGetValue(snapshot.SessionId.Value, out session))
        {
            issue = Issue("session_unavailable", "The code-intelligence session is unavailable.");
            return false;
        }

        if (snapshot.Path is null || !IsConfinedRelativePath(snapshot.Path.Value) ||
            snapshot.BaselineHash is null || !IsSha256(snapshot.BaselineHash.Value) ||
            snapshot.BufferVersion is null || snapshot.BufferVersion.Value <= 0 ||
            snapshot.Text is null)
        {
            issue = Issue(
                "invalid_snapshot",
                "A confined path, exact baseline, positive buffer version, and text are required.");
            return false;
        }

        issue = null;
        return true;
    }

    private static CodeIntelligenceContextId CreateContextId(
        WorkbenchWorkspaceResolution resolution,
        WorkbenchCodeEntryPoint entryPoint)
    {
        string identity = string.Join(
            '\n',
            resolution.Context.WorkspaceId.Value,
            resolution.Context.GoalId?.Value ?? string.Empty,
            resolution.Context.Scope.ToString(),
            Path.GetFullPath(resolution.RootPath!),
            entryPoint.Value);
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
    }

    private static bool IsSupportedEntryPoint(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".slnx" or ".sln" or
            ".csproj" or ".fsproj" or ".vbproj";

    private static bool IsConfinedRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        string normalized = path.Replace('\\', '/');
        return !normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsValidDiagnostic(CodeIntelligenceDiagnostic diagnostic) =>
        IsConfinedRelativePath(diagnostic.Path.Value) &&
        diagnostic.Range.Start.Line >= 0 && diagnostic.Range.Start.Character >= 0 &&
        diagnostic.Range.End.Line >= diagnostic.Range.Start.Line &&
        diagnostic.Range.End.Character >= 0 &&
        (diagnostic.Range.End.Line > diagnostic.Range.Start.Line ||
         diagnostic.Range.End.Character >= diagnostic.Range.Start.Character);

    private static WorkbenchCodeSessionView SessionFailure(string code, string message) => new(
        null,
        null,
        WorkbenchCodeResultState.Failed,
        [Issue(code, message)]);

    private static WorkbenchCodeDiagnosticView DiagnosticFailure(
        WorkbenchCodeDocumentSnapshot snapshot,
        WorkbenchCodeResultState state,
        WorkbenchCodeIssue issue) => new(
        snapshot.SessionId ?? new(string.Empty),
        snapshot.Path ?? new(string.Empty),
        snapshot.BufferVersion ?? new(0),
        state,
        [],
        [issue]);

    private static WorkbenchCodeValidationView ValidationFailure(
        WorkbenchCodeSessionId sessionId,
        string code,
        string message) => new(
        sessionId,
        WorkbenchCodeResultState.Failed,
        WorkbenchCodeValidationDisposition.Rejected,
        [],
        [Issue(code, message)]);

    private static WorkbenchCodeIssue Issue(string code, string message) => new(
        new(code),
        new(message));

    private static IReadOnlyList<WorkbenchCodeIssue> MapIssues(
        IReadOnlyList<CodeIntelligenceIssue> issues) => issues
        .Take(MaximumIssues)
        .Select(issue => Issue(
            issue.Code.Value,
            issue.Message.Value.Length <= MaximumIssueMessageLength
                ? issue.Message.Value
                : issue.Message.Value[..MaximumIssueMessageLength]))
        .ToArray();

    private static WorkbenchCodeResultState Map(CodeIntelligenceResultState state) => state switch
    {
        CodeIntelligenceResultState.Ready => WorkbenchCodeResultState.Ready,
        CodeIntelligenceResultState.Loading => WorkbenchCodeResultState.Loading,
        CodeIntelligenceResultState.Degraded => WorkbenchCodeResultState.Degraded,
        CodeIntelligenceResultState.Cancelled => WorkbenchCodeResultState.Cancelled,
        CodeIntelligenceResultState.Failed => WorkbenchCodeResultState.Failed,
        CodeIntelligenceResultState.Stale => WorkbenchCodeResultState.Stale,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static WorkbenchCodeLoadStage Map(CodeIntelligenceLoadStage stage) => stage switch
    {
        CodeIntelligenceLoadStage.SelectingSdk => WorkbenchCodeLoadStage.SelectingSdk,
        CodeIntelligenceLoadStage.RegisteringMSBuild => WorkbenchCodeLoadStage.RegisteringMSBuild,
        CodeIntelligenceLoadStage.LoadingEntryPoint => WorkbenchCodeLoadStage.LoadingEntryPoint,
        CodeIntelligenceLoadStage.EvaluatingProjects => WorkbenchCodeLoadStage.EvaluatingProjects,
        CodeIntelligenceLoadStage.Ready => WorkbenchCodeLoadStage.Ready,
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    private static WorkbenchCodeDiagnostic Map(CodeIntelligenceDiagnostic diagnostic) => new(
        new(diagnostic.Id.Value),
        new(diagnostic.Message.Value.Length <= MaximumIssueMessageLength
            ? diagnostic.Message.Value
            : diagnostic.Message.Value[..MaximumIssueMessageLength]),
        new(diagnostic.Source.Value),
        diagnostic.Project is null ? null : new(diagnostic.Project.Value),
        new(diagnostic.Path.Value),
        new(
            new(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character),
            new(diagnostic.Range.End.Line, diagnostic.Range.End.Character)),
        diagnostic.Severity switch
        {
            CodeIntelligenceDiagnosticSeverity.Hidden => WorkbenchCodeDiagnosticSeverity.Hidden,
            CodeIntelligenceDiagnosticSeverity.Information => WorkbenchCodeDiagnosticSeverity.Information,
            CodeIntelligenceDiagnosticSeverity.Warning => WorkbenchCodeDiagnosticSeverity.Warning,
            CodeIntelligenceDiagnosticSeverity.Error => WorkbenchCodeDiagnosticSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(diagnostic)),
        });

    private static WorkbenchCodeValidationDisposition Map(
        CodeIntelligenceValidationDisposition disposition) => disposition switch
    {
        CodeIntelligenceValidationDisposition.Validated => WorkbenchCodeValidationDisposition.Validated,
        CodeIntelligenceValidationDisposition.Rejected => WorkbenchCodeValidationDisposition.Rejected,
        CodeIntelligenceValidationDisposition.NotApplicable =>
            WorkbenchCodeValidationDisposition.NotApplicable,
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };

    private static WorkbenchCodeDiagnosticDeltaKind Map(
        CodeIntelligenceDiagnosticDeltaKind kind) => kind switch
    {
        CodeIntelligenceDiagnosticDeltaKind.Retained => WorkbenchCodeDiagnosticDeltaKind.Retained,
        CodeIntelligenceDiagnosticDeltaKind.Resolved => WorkbenchCodeDiagnosticDeltaKind.Resolved,
        CodeIntelligenceDiagnosticDeltaKind.Introduced => WorkbenchCodeDiagnosticDeltaKind.Introduced,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class ActiveSession(
        CodeIntelligenceContextId contextId,
        CodeIntelligenceSessionId sessionId,
        CodeIntelligenceSourceKind sourceKind)
    {
        internal object Gate { get; } = new();
        internal Dictionary<string, long> DocumentVersions { get; } =
            new(StringComparer.Ordinal);
        internal CodeIntelligenceContextId ContextId { get; } = contextId;
        internal CodeIntelligenceSessionId SessionId { get; } = sessionId;
        internal CodeIntelligenceSourceKind SourceKind { get; } = sourceKind;
    }

    private sealed class LoadProgressAdapter(IProgress<WorkbenchCodeLoadProgress> progress)
        : IProgress<CodeIntelligenceLoadProgress>
    {
        public void Report(CodeIntelligenceLoadProgress value) => progress.Report(new(
            new(value.ContextId.Value),
            Map(value.Stage),
            new(value.Message.Value)));
    }
}
