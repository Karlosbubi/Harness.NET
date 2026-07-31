using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed class RoslynCodeIntelligenceEngine(IMSBuildRuntime msBuildRuntime)
    : ICodeIntelligenceEngine, IDisposable
{
    private const int MaximumIssues = 100;
    private const int MaximumIssueLength = 2_048;
    private const int MaximumDiagnostics = 5_000;
    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private ActiveSession? activeSession;
    private bool disposed;

    public async ValueTask<CodeIntelligenceSessionResult> OpenAsync(
        CodeIntelligenceOpenRequest request,
        IProgress<CodeIntelligenceLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        progress?.Report(Progress(
            request.ContextId,
            CodeIntelligenceLoadStage.SelectingSdk,
            "Resolving the workspace's installed .NET SDK."));
        MSBuildRuntimeResult runtime = await msBuildRuntime.EnsureRegisteredAsync(
            request.RootPath.Value,
            cancellationToken);
        if (runtime.State is not MSBuildRuntimeState.Ready)
        {
            return Failure(
                request.ContextId,
                runtime.State is MSBuildRuntimeState.Failed
                    ? CodeIntelligenceResultState.Failed
                    : CodeIntelligenceResultState.Degraded,
                runtime.ErrorCode ?? "sdk_unavailable",
                runtime.Error ?? "The workspace SDK is unavailable.");
        }

        progress?.Report(Progress(
            request.ContextId,
            CodeIntelligenceLoadStage.RegisteringMSBuild,
            $"Using .NET SDK {runtime.SdkVersion!.Value}."));
        if (!TryResolveEntryPoint(request, out string root, out string entryPoint))
        {
            return Failure(
                request.ContextId,
                CodeIntelligenceResultState.Failed,
                "invalid_entry_point",
                "The code-intelligence entry point is missing or outside the source context.");
        }

        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (activeSession is { } existing &&
                existing.ContextId == request.ContextId &&
                existing.EntryPoint.Equals(entryPoint, StringComparison.Ordinal))
            {
                return existing.AsResult();
            }

            ActiveSession? previous = activeSession;
            activeSession = null;
            if (previous is not null)
            {
                await previous.DisposeAsync();
            }
            progress?.Report(Progress(
                request.ContextId,
                CodeIntelligenceLoadStage.LoadingEntryPoint,
                $"Loading {Path.GetFileName(entryPoint)} without restoring packages."));
            try
            {
                ActiveSession loaded = await LoadRegisteredAsync(
                    request,
                    root,
                    entryPoint,
                    progress,
                    cancellationToken);
                activeSession = loaded;
                return loaded.AsResult();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsWorkspaceLoadFailure(exception))
            {
                return Failure(
                    request.ContextId,
                    CodeIntelligenceResultState.Failed,
                    "workspace_load_failed",
                    exception.Message);
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceDiagnosticResult> GetDiagnosticsAsync(
        CodeIntelligenceDocumentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = activeSession;
        if (session is null || session.SessionId != snapshot.SessionId ||
            session.ContextId != snapshot.ContextId)
        {
            return DiagnosticFailure(
                snapshot,
                CodeIntelligenceResultState.Stale,
                "session_unavailable",
                "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            if (activeSession != session)
            {
                return DiagnosticFailure(
                    snapshot,
                    CodeIntelligenceResultState.Stale,
                    "session_replaced",
                    "The Roslyn session was replaced while diagnostics were queued.");
            }

            if (!TryResolveDocumentPath(session.RootPath, snapshot.Path.Value, out string path))
            {
                return DiagnosticFailure(
                    snapshot,
                    CodeIntelligenceResultState.Failed,
                    "invalid_document_path",
                    "The document path is outside the source context.");
            }

            Document? document = session.CurrentSolution.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(candidate => candidate.FilePath is not null &&
                    Path.GetFullPath(candidate.FilePath).Equals(path, StringComparison.Ordinal));
            if (document is null)
            {
                return DiagnosticFailure(
                    snapshot,
                    CodeIntelligenceResultState.Degraded,
                    "document_not_in_workspace",
                    "The document is not represented by the loaded .NET workspace.");
            }

            CodeIntelligenceIssue? baselineIssue = await VerifyBaselineAsync(
                path,
                snapshot.BaselineHash,
                cancellationToken);
            if (baselineIssue is not null)
            {
                return new(
                    snapshot.ContextId,
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    CodeIntelligenceResultState.Stale,
                    [],
                    [baselineIssue]);
            }

            SourceText source = SourceText.From(snapshot.Text.Value, Utf8WithoutBom);
            Solution candidate = session.CurrentSolution.WithDocumentText(
                document.Id,
                source,
                PreservationMode.PreserveIdentity);
            Document candidateDocument = candidate.GetDocument(document.Id)!;
            Project project = candidateDocument.Project;
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                return DiagnosticFailure(
                    snapshot,
                    CodeIntelligenceResultState.Degraded,
                    "compilation_unavailable",
                    $"Project {project.Name} did not produce a compilation.");
            }

            ImmutableArray<DiagnosticAnalyzer> analyzers = project.AnalyzerReferences
                .SelectMany(reference => reference.GetAnalyzers(project.Language))
                .ToImmutableArray();
            ImmutableArray<Diagnostic> diagnostics = analyzers.IsEmpty
                ? compilation.GetDiagnostics(cancellationToken)
                : await compilation
                    .WithAnalyzers(analyzers, project.AnalyzerOptions)
                    .GetAllDiagnosticsAsync(cancellationToken);
            session.CurrentSolution = candidate;
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                session.Issues.IsEmpty
                    ? CodeIntelligenceResultState.Ready
                    : CodeIntelligenceResultState.Degraded,
                diagnostics
                    .Where(diagnostic => IsForDocument(diagnostic, path))
                    .Take(MaximumDiagnostics)
                    .Select(diagnostic => MapDiagnostic(
                        diagnostic,
                        project.Name,
                        session.RootPath,
                        snapshot.Path))
                    .ToArray(),
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException)
        {
            return DiagnosticFailure(
                snapshot,
                CodeIntelligenceResultState.Failed,
                "diagnostics_failed",
                exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public ValueTask<CodeIntelligenceValidationResult> ValidateAsync(
        CodeIntelligenceValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CodeIntelligenceValidationResult(
            request.ContextId,
            request.SessionId,
            CodeIntelligenceResultState.Degraded,
            CodeIntelligenceValidationDisposition.Rejected,
            [],
            [Issue(
                "validation_not_ready",
                "Roslyn candidate validation is not connected to the durable mutation boundary yet.")]));
    }

    public async ValueTask CloseAsync(
        CodeIntelligenceSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (activeSession?.SessionId == sessionId)
            {
                ActiveSession closing = activeSession;
                activeSession = null;
                await closing.DisposeAsync();
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ActiveSession? closing = activeSession;
        activeSession = null;
        closing?.Dispose();
        lifecycleGate.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async ValueTask<ActiveSession> LoadRegisteredAsync(
        CodeIntelligenceOpenRequest request,
        string root,
        string entryPoint,
        IProgress<CodeIntelligenceLoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ConcurrentQueue<CodeIntelligenceIssue> issues = new();
        Dictionary<string, string> properties = new(StringComparer.Ordinal)
        {
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["SkipCompilerExecution"] = "true",
        };
        MSBuildWorkspace workspace = MSBuildWorkspace.Create(properties);
        IDisposable workspaceFailure = workspace.RegisterWorkspaceFailedHandler(args =>
            EnqueueIssue(
                issues,
                args.Diagnostic.Kind.ToString().ToLowerInvariant(),
                args.Diagnostic.Message));
        try
        {
            Solution solution = Path.GetExtension(entryPoint).ToLowerInvariant() switch
            {
                ".sln" or ".slnx" => await workspace.OpenSolutionAsync(
                    entryPoint,
                    progress: null,
                    cancellationToken),
                ".csproj" or ".fsproj" or ".vbproj" =>
                    (await workspace.OpenProjectAsync(
                        entryPoint,
                        progress: null,
                        cancellationToken)).Solution,
                _ => throw new ArgumentException("Unsupported code-intelligence entry point."),
            };
            if (solution.ProjectIds.Count == 0)
            {
                throw new InvalidOperationException(
                    issues.TryPeek(out CodeIntelligenceIssue? issue)
                        ? issue.Message.Value
                        : "The entry point did not load any projects.");
            }

            progress?.Report(Progress(
                request.ContextId,
                CodeIntelligenceLoadStage.EvaluatingProjects,
                $"Loaded {solution.ProjectIds.Count} project(s); preparing compiler state."));
            foreach (Project project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = await project.GetCompilationAsync(cancellationToken);
            }

            progress?.Report(Progress(
                request.ContextId,
                CodeIntelligenceLoadStage.Ready,
                issues.IsEmpty
                    ? "Code intelligence is ready."
                    : "Code intelligence is ready with workspace issues."));
            return new(
                request.ContextId,
                new(Guid.NewGuid().ToString("N")),
                request.SourceKind,
                root,
                entryPoint,
                workspace,
                workspaceFailure,
                solution,
                issues);
        }
        catch
        {
            workspaceFailure.Dispose();
            workspace.Dispose();
            throw;
        }
    }

    private static bool TryResolveEntryPoint(
        CodeIntelligenceOpenRequest request,
        out string root,
        out string entryPoint)
    {
        try
        {
            root = Path.GetFullPath(request.RootPath.Value);
            entryPoint = Path.IsPathRooted(request.EntryPoint.Value)
                ? Path.GetFullPath(request.EntryPoint.Value)
                : Path.GetFullPath(request.EntryPoint.Value, root);
            string relative = Path.GetRelativePath(root, entryPoint);
            return relative != ".." &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                File.Exists(entryPoint);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            root = string.Empty;
            entryPoint = string.Empty;
            return false;
        }
    }

    private static bool TryResolveDocumentPath(string root, string relativePath, out string path)
    {
        path = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.GetFullPath(relativePath, root);
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            File.Exists(path);
    }

    private static async ValueTask<CodeIntelligenceIssue?> VerifyBaselineAsync(
        string path,
        CodeIntelligenceBaselineHash baseline,
        CancellationToken cancellationToken)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(path, Utf8WithoutBom, cancellationToken);
        }
        catch (DecoderFallbackException exception)
        {
            return Issue("document_encoding_unsupported", exception.Message);
        }

        string current = Convert.ToHexStringLower(SHA256.HashData(
            Utf8WithoutBom.GetBytes(content)));
        return current.Equals(baseline.Value, StringComparison.OrdinalIgnoreCase)
            ? null
            : Issue(
                "baseline_changed",
                "The persisted document changed after this editor snapshot was created.");
    }

    private static bool IsForDocument(Diagnostic diagnostic, string path) =>
        diagnostic.Location.IsInSource &&
        diagnostic.Location.SourceTree?.FilePath is { } diagnosticPath &&
        Path.GetFullPath(diagnosticPath).Equals(path, StringComparison.Ordinal);

    private static CodeIntelligenceDiagnostic MapDiagnostic(
        Diagnostic diagnostic,
        string project,
        string root,
        CodeIntelligenceDocumentPath fallbackPath)
    {
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        string path = diagnostic.Location.SourceTree?.FilePath is { } sourcePath
            ? Path.GetRelativePath(root, sourcePath)
            : fallbackPath.Value;
        return new(
            new(diagnostic.Id),
            new(Bound(diagnostic.GetMessage(), MaximumIssueLength)),
            new(diagnostic.Id.StartsWith("CS", StringComparison.Ordinal)
                ? "Compiler"
                : "Analyzer"),
            new(project),
            new(path),
            new(
                new(span.StartLinePosition.Line, span.StartLinePosition.Character),
                new(span.EndLinePosition.Line, span.EndLinePosition.Character)),
            diagnostic.Severity switch
            {
                DiagnosticSeverity.Hidden => CodeIntelligenceDiagnosticSeverity.Hidden,
                DiagnosticSeverity.Info => CodeIntelligenceDiagnosticSeverity.Information,
                DiagnosticSeverity.Warning => CodeIntelligenceDiagnosticSeverity.Warning,
                DiagnosticSeverity.Error => CodeIntelligenceDiagnosticSeverity.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(diagnostic)),
            });
    }

    private static CodeIntelligenceLoadProgress Progress(
        CodeIntelligenceContextId contextId,
        CodeIntelligenceLoadStage stage,
        string message) => new(contextId, stage, new(message));

    private static CodeIntelligenceSessionResult Failure(
        CodeIntelligenceContextId contextId,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(contextId, null, state, [Issue(code, message)]);

    private static CodeIntelligenceDiagnosticResult DiagnosticFailure(
        CodeIntelligenceDocumentSnapshot snapshot,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        [],
        [Issue(code, message)]);

    private static CodeIntelligenceIssue Issue(string code, string message) => new(
        new(code),
        new(Bound(message, MaximumIssueLength)));

    private static void EnqueueIssue(
        ConcurrentQueue<CodeIntelligenceIssue> issues,
        string code,
        string message)
    {
        if (issues.Count < MaximumIssues)
        {
            issues.Enqueue(Issue(code, message));
        }
    }

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static bool IsWorkspaceLoadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException ||
        exception.GetType().FullName == "Microsoft.Build.Exceptions.InvalidProjectFileException";

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private sealed class ActiveSession(
        CodeIntelligenceContextId contextId,
        CodeIntelligenceSessionId sessionId,
        CodeIntelligenceSourceKind sourceKind,
        string rootPath,
        string entryPoint,
        MSBuildWorkspace workspace,
        IDisposable workspaceFailure,
        Solution solution,
        ConcurrentQueue<CodeIntelligenceIssue> issues) : IDisposable
    {
        internal CodeIntelligenceContextId ContextId { get; } = contextId;
        internal CodeIntelligenceSessionId SessionId { get; } = sessionId;
        internal CodeIntelligenceSourceKind SourceKind { get; } = sourceKind;
        internal string RootPath { get; } = rootPath;
        internal string EntryPoint { get; } = entryPoint;
        internal MSBuildWorkspace Workspace { get; } = workspace;
        internal IDisposable WorkspaceFailure { get; } = workspaceFailure;
        internal ConcurrentQueue<CodeIntelligenceIssue> Issues { get; } = issues;
        internal SemaphoreSlim OperationGate { get; } = new(1, 1);
        internal Solution CurrentSolution { get; set; } = solution;

        internal CodeIntelligenceSessionResult AsResult() => new(
            ContextId,
            SessionId,
            Issues.IsEmpty
                ? CodeIntelligenceResultState.Ready
                : CodeIntelligenceResultState.Degraded,
            Issues.ToArray());

        public void Dispose()
        {
            OperationGate.Wait();
            try
            {
                WorkspaceFailure.Dispose();
                Workspace.Dispose();
            }
            finally
            {
                OperationGate.Release();
            }
        }

        internal async ValueTask DisposeAsync()
        {
            await OperationGate.WaitAsync();
            try
            {
                WorkspaceFailure.Dispose();
                Workspace.Dispose();
            }
            finally
            {
                OperationGate.Release();
            }
        }
    }
}
