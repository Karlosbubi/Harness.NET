using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine(IMSBuildRuntime msBuildRuntime)
    : ICodeIntelligenceEngine, IDisposable
{
    private const int MaximumIssues = 100;
    private const int MaximumIssueLength = 2_048;
    private const int MaximumDiagnostics = 5_000;
    private const int MaximumCompletionItems = 200;
    private const int MaximumNavigationItems = 500;
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

    public async ValueTask<CodeIntelligenceValidationResult> ValidateAsync(
        CodeIntelligenceValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveSession? session = activeSession;
        if (session is null || session.SessionId != request.SessionId ||
            session.ContextId != request.ContextId)
        {
            return ValidationFailure(
                request,
                CodeIntelligenceResultState.Stale,
                "session_unavailable",
                "The Roslyn session no longer matches this source context.");
        }

        if (!Enum.IsDefined(request.Phase) || request.Edits.Count == 0)
        {
            return ValidationFailure(
                request,
                CodeIntelligenceResultState.Failed,
                "invalid_validation_request",
                "A valid phase and at least one candidate edit are required.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            if (activeSession != session)
            {
                return ValidationFailure(
                    request,
                    CodeIntelligenceResultState.Stale,
                    "session_replaced",
                    "The Roslyn session was replaced while validation was queued.");
            }

            Solution baseline = session.PersistedSolution;
            Dictionary<CodeIntelligenceDocumentPath, IReadOnlyList<DocumentId>> documents = [];
            HashSet<ProjectId> affectedProjects = [];
            foreach (CodeIntelligenceCandidateEdit edit in request.Edits)
            {
                if (!TryResolveDocumentPath(session.RootPath, edit.Path.Value, out string path))
                {
                    return ValidationFailure(
                        request,
                        CodeIntelligenceResultState.Failed,
                        "invalid_document_path",
                        "A candidate document path is missing or outside the source context.");
                }

                IReadOnlyList<DocumentId> matching = baseline.Projects
                    .SelectMany(project => project.Documents)
                    .Where(document => document.FilePath is not null &&
                        Path.GetFullPath(document.FilePath).Equals(path, StringComparison.Ordinal))
                    .Select(document => document.Id)
                    .ToArray();
                if (matching.Count == 0)
                {
                    if (request.Edits.Count == 1)
                    {
                        return new(
                            request.ContextId,
                            request.SessionId,
                            CodeIntelligenceResultState.Ready,
                            CodeIntelligenceValidationDisposition.NotApplicable,
                            [],
                            [Issue(
                                "document_not_in_workspace",
                                "The changed file is not represented by the loaded compiler workspace.")]);
                    }

                    return ValidationFailure(
                        request,
                        CodeIntelligenceResultState.Failed,
                        "mixed_validation_scope",
                        "A candidate batch cannot mix compiler documents with unsupported files.");
                }

                string persistedText;
                try
                {
                    persistedText = await File.ReadAllTextAsync(path, Utf8WithoutBom, cancellationToken);
                }
                catch (DecoderFallbackException exception)
                {
                    return ValidationFailure(
                        request,
                        CodeIntelligenceResultState.Failed,
                        "document_encoding_unsupported",
                        exception.Message);
                }

                string persistedHash = Hash(persistedText);
                if (!persistedHash.Equals(edit.BaselineHash.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return ValidationFailure(
                        request,
                        CodeIntelligenceResultState.Stale,
                        "baseline_changed",
                        "A persisted document changed after the candidate baseline was created.");
                }

                if (request.Phase is CodeIntelligenceValidationPhase.Applied &&
                    !Hash(edit.Text.Value).Equals(persistedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return ValidationFailure(
                        request,
                        CodeIntelligenceResultState.Stale,
                        "applied_content_mismatch",
                        "The applied document does not match the validated candidate text.");
                }

                SourceText persistedSource = SourceText.From(persistedText, Utf8WithoutBom);
                foreach (DocumentId documentId in matching)
                {
                    baseline = baseline.WithDocumentText(
                        documentId,
                        persistedSource,
                        PreservationMode.PreserveIdentity);
                    affectedProjects.Add(documentId.ProjectId);
                }

                documents.Add(edit.Path, matching);
            }

            session.PersistedSolution = baseline;
            Solution candidate = baseline;
            foreach (CodeIntelligenceCandidateEdit edit in request.Edits)
            {
                SourceText candidateSource = SourceText.From(edit.Text.Value, Utf8WithoutBom);
                foreach (DocumentId documentId in documents[edit.Path])
                {
                    candidate = candidate.WithDocumentText(
                        documentId,
                        candidateSource,
                        PreservationMode.PreserveIdentity);
                }
            }

            ProjectDependencyGraph dependencyGraph = candidate.GetProjectDependencyGraph();
            foreach (ProjectId projectId in affectedProjects.ToArray())
            {
                affectedProjects.UnionWith(
                    dependencyGraph.GetProjectsThatTransitivelyDependOnThisProject(projectId));
            }

            IReadOnlyList<CollectedDiagnostic> baselineDiagnostics =
                await CollectDiagnosticsAsync(baseline, affectedProjects, session.RootPath, cancellationToken);
            IReadOnlyList<CollectedDiagnostic> candidateDiagnostics =
                await CollectDiagnosticsAsync(candidate, affectedProjects, session.RootPath, cancellationToken);
            IReadOnlyList<CodeIntelligenceValidationDiagnostic> delta = CompareDiagnostics(
                baselineDiagnostics,
                candidateDiagnostics);
            bool introducedCompilerError = delta.Any(item =>
                item.Kind is CodeIntelligenceDiagnosticDeltaKind.Introduced &&
                item.Diagnostic.Source.Value.Equals("Compiler", StringComparison.Ordinal) &&
                item.Diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Error);

            if (request.Phase is CodeIntelligenceValidationPhase.Applied &&
                !introducedCompilerError)
            {
                session.PersistedSolution = candidate;
                Solution live = session.CurrentSolution;
                foreach (CodeIntelligenceCandidateEdit edit in request.Edits)
                {
                    SourceText appliedSource = SourceText.From(edit.Text.Value, Utf8WithoutBom);
                    foreach (DocumentId documentId in documents[edit.Path])
                    {
                        live = live.WithDocumentText(
                            documentId,
                            appliedSource,
                            PreservationMode.PreserveIdentity);
                    }
                }

                session.CurrentSolution = live;
            }

            return new(
                request.ContextId,
                request.SessionId,
                session.Issues.IsEmpty
                    ? CodeIntelligenceResultState.Ready
                    : CodeIntelligenceResultState.Degraded,
                introducedCompilerError
                    ? CodeIntelligenceValidationDisposition.Rejected
                    : CodeIntelligenceValidationDisposition.Validated,
                delta.Take(MaximumDiagnostics).ToArray(),
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
            return ValidationFailure(
                request,
                CodeIntelligenceResultState.Failed,
                "validation_failed",
                exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceCompletionResult> GetCompletionsAsync(
        CodeIntelligenceCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveSession? session = MatchingSession(request.Snapshot);
        if (session is null)
        {
            return CompletionFailure(request.Snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, request.Snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return CompletionFailure(
                    request.Snapshot, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            CompletionService? service = CompletionService.GetService(prepared.Document!);
            if (service is null)
            {
                return CompletionFailure(request.Snapshot, CodeIntelligenceResultState.Degraded,
                    "completion_unavailable", "Completion is unavailable for this document.");
            }

            CompletionTrigger trigger = request.TriggerKind switch
            {
                CodeIntelligenceCompletionTriggerKind.Invoke => CompletionTrigger.Invoke,
                CodeIntelligenceCompletionTriggerKind.Insertion when request.TriggerCharacter is { } value =>
                    CompletionTrigger.CreateInsertionTrigger(value),
                CodeIntelligenceCompletionTriggerKind.Insertion => CompletionTrigger.Invoke,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
            CompletionList? list = await service.GetCompletionsAsync(
                prepared.Document!, prepared.Offset, trigger, cancellationToken: cancellationToken);
            if (list is null)
            {
                return new(
                    request.Snapshot.ContextId,
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    SessionState(session),
                    null,
                    Range(prepared.Text!, new TextSpan(prepared.Offset, 0)),
                    [],
                    session.Issues.ToArray());
            }

            CodeIntelligenceCompletionListId listId = new(Guid.NewGuid().ToString("N"));
            Dictionary<CodeIntelligenceCompletionItemId, CompletionItem> cachedItems = [];
            List<CodeIntelligenceCompletionItem> items = [];
            int index = 0;
            foreach (CompletionItem item in list.ItemsList.Take(MaximumCompletionItems))
            {
                CodeIntelligenceCompletionItemId itemId = new((index++).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                cachedItems.Add(itemId, item);
                items.Add(new(
                    itemId,
                    new(Bound(item.DisplayText + item.DisplayTextSuffix, MaximumIssueLength)),
                    new(Bound(item.FilterText, MaximumIssueLength)),
                    new(Bound(item.SortText, MaximumIssueLength)),
                    new(Bound(item.InlineDescription ?? string.Empty, MaximumIssueLength)),
                    MapSymbolKind(item.Tags),
                    CommitCharacters(item.Rules),
                    IsRecommended: false));
            }

            session.CompletionCache = new(
                listId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                Hash(request.Snapshot.Text.Value),
                service,
                cachedItems);
            return new(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                SessionState(session),
                listId,
                Range(prepared.Text!, list.Span),
                items,
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
            return CompletionFailure(request.Snapshot, CodeIntelligenceResultState.Failed,
                "completion_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceCompletionCommitResult> CommitCompletionAsync(
        CodeIntelligenceCompletionCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveSession? session = MatchingSession(request.Snapshot);
        if (session is null)
        {
            return CommitFailure(request.Snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, request.Snapshot, cancellationToken);
            CompletionCache? cache = session.CompletionCache;
            if (prepared.Issue is not null || cache is null || cache.ListId != request.ListId ||
                cache.Path != request.Snapshot.Path ||
                cache.BufferVersion != request.Snapshot.BufferVersion ||
                !cache.TextHash.Equals(Hash(request.Snapshot.Text.Value), StringComparison.Ordinal) ||
                !cache.Items.TryGetValue(request.ItemId, out CompletionItem? item))
            {
                return CommitFailure(
                    request.Snapshot,
                    prepared.Issue is null
                        ? CodeIntelligenceResultState.Stale
                        : prepared.State,
                    prepared.Issue?.Code.Value ?? "completion_stale",
                    prepared.Issue?.Message.Value ??
                        "The completion list no longer matches the active buffer.");
            }

            CompletionChange change = await cache.Service.GetChangeAsync(
                prepared.Document!, item, request.CommitCharacter, cancellationToken);
            return new(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                SessionState(session),
                [new(
                    Range(prepared.Text!, change.TextChange.Span),
                    new(change.TextChange.NewText ?? string.Empty))],
                change.NewPosition is { } position
                    ? Position(prepared.Text!, position)
                    : null,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return CommitFailure(request.Snapshot, CodeIntelligenceResultState.Failed,
                "completion_commit_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceQuickInfoResult> GetQuickInfoAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return QuickInfoFailure(snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return QuickInfoFailure(
                    snapshot, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            QuickInfoService? service = QuickInfoService.GetService(prepared.Document!);
            QuickInfoItem? item = service is null
                ? null
                : await service.GetQuickInfoAsync(
                    prepared.Document!, prepared.Offset, cancellationToken);
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                item is null ? null : Range(prepared.Text!, item.Span),
                item?.Sections
                    .Select(section => new CodeIntelligenceMessage(Bound(
                        string.Concat(section.TaggedParts.Select(part => part.Text)),
                        MaximumIssueLength)))
                    .Where(section => !string.IsNullOrWhiteSpace(section.Value))
                    .Take(12)
                    .ToArray() ?? [],
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
            return QuickInfoFailure(snapshot, CodeIntelligenceResultState.Failed,
                "quick_info_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceSignatureHelpResult> GetSignatureHelpAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return SignatureFailure(snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return SignatureFailure(snapshot, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            SyntaxNode? root = await prepared.Document!.GetSyntaxRootAsync(cancellationToken);
            SemanticModel? model = await prepared.Document.GetSemanticModelAsync(cancellationToken);
            SyntaxNode? node = root?.FindToken(Math.Max(0, prepared.Offset - 1)).Parent;
            BaseArgumentListSyntax? arguments = node?.AncestorsAndSelf()
                .OfType<BaseArgumentListSyntax>()
                .FirstOrDefault();
            SyntaxNode? callable = arguments?.Parent;
            SymbolInfo symbolInfo = callable is null || model is null
                ? default
                : model.GetSymbolInfo(callable, cancellationToken);
            IReadOnlyList<IMethodSymbol> methods = (symbolInfo.Symbol is IMethodSymbol method
                    ? [method]
                    : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>())
                .Cast<ISymbol>()
                .Distinct(SymbolEqualityComparer.Default)
                .OfType<IMethodSymbol>()
                .Take(12)
                .ToArray();
            int selectedParameter = arguments?.Arguments.GetSeparators()
                .Count(separator => separator.SpanStart < prepared.Offset) ?? 0;
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                methods.Select(method => MapSignature(method, cancellationToken)).ToArray(),
                SelectedSignature: 0,
                selectedParameter,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return SignatureFailure(snapshot, CodeIntelligenceResultState.Failed,
                "signature_help_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public ValueTask<CodeIntelligenceNavigationResult> FindDefinitionAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(snapshot, NavigationKind.Definition, cancellationToken);

    public ValueTask<CodeIntelligenceNavigationResult> FindReferencesAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(snapshot, NavigationKind.References, cancellationToken);

    public ValueTask<CodeIntelligenceNavigationResult> FindImplementationsAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(snapshot, NavigationKind.Implementations, cancellationToken);

    public ValueTask<CodeIntelligenceSemanticResult> SearchSymbolsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Symbols, cancellationToken);

    public ValueTask<CodeIntelligenceSemanticResult> AnalyzeCallsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Calls, cancellationToken);

    public ValueTask<CodeIntelligenceSemanticResult> GetTypeHierarchyAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Types, cancellationToken);

    public ValueTask<CodeIntelligenceSemanticResult> FindAssociatedTestsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Tests, cancellationToken);

    private async ValueTask<CodeIntelligenceSemanticResult> SemanticAsync(
        CodeIntelligenceSemanticQuery query,
        SemanticKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaximumResults is < 1 or > 200 || query.Offset < 0 ||
            query.Query?.Length > 256)
            return SemanticFailure(query, "invalid_semantic_query",
                "Result limit, continuation offset, or query is outside the bounded range.");
        ActiveSession? session = MatchingSession(query.Snapshot);
        if (session is null)
            return SemanticFailure(query, "session_unavailable",
                "The Roslyn session no longer matches this source context.", CodeIntelligenceResultState.Stale);

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, query.Snapshot, cancellationToken);
            if (prepared.Issue is not null)
                return SemanticFailure(query, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value, prepared.State);
            Solution solution = prepared.Document!.Project.Solution;
            List<CodeIntelligenceSemanticItem> items = [];
            if (kind is SemanticKind.Symbols)
            {
                if (string.IsNullOrWhiteSpace(query.Query))
                    return SemanticFailure(query, "symbol_query_required", "A symbol name is required.");
                List<ISymbol> symbols = [];
                foreach (Project project in solution.Projects)
                    symbols.AddRange(await SymbolFinder.FindDeclarationsAsync(
                        project, query.Query, ignoreCase: true, filter: SymbolFilter.TypeAndMember,
                        cancellationToken: cancellationToken));
                foreach (ISymbol symbol in symbols.Take(query.Offset + query.MaximumResults + 1))
                    AddSymbol(items, CodeIntelligenceSemanticRelation.Symbol, symbol, session.RootPath);
            }
            else
            {
                ISymbol? symbol = await SymbolFinder.FindSymbolAtPositionAsync(
                    prepared.Document, prepared.Offset, cancellationToken);
                if (symbol is null)
                    return SemanticFailure(query, "symbol_unavailable",
                        "No symbol is available at the requested position.");
                if (kind is SemanticKind.Calls)
                {
                    IEnumerable<SymbolCallerInfo> callers = await SymbolFinder.FindCallersAsync(
                        symbol, solution, cancellationToken: cancellationToken);
                    foreach (ISymbol caller in callers.Select(item => item.CallingSymbol)
                                 .Distinct(SymbolEqualityComparer.Default))
                        AddSymbol(items, CodeIntelligenceSemanticRelation.IncomingCall, caller, session.RootPath);
                    foreach (SyntaxReference syntaxReference in symbol.DeclaringSyntaxReferences)
                    {
                        SyntaxNode declaration = await syntaxReference.GetSyntaxAsync(cancellationToken);
                        Document? document = solution.GetDocument(declaration.SyntaxTree);
                        if (document is null) continue;
                        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken);
                        if (model is null) continue;
                        foreach (InvocationExpressionSyntax invocation in declaration
                                     .DescendantNodes().OfType<InvocationExpressionSyntax>())
                        {
                            ISymbol? called = model.GetSymbolInfo(invocation, cancellationToken).Symbol;
                            if (called is not null)
                                AddSymbol(items, CodeIntelligenceSemanticRelation.OutgoingCall,
                                    called, session.RootPath);
                        }
                    }
                }
                else if (kind is SemanticKind.Types)
                {
                    INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
                    if (type is null)
                        return SemanticFailure(query, "type_unavailable",
                            "The selected symbol has no containing type.");
                    if (type.BaseType is not null)
                        AddSymbol(items, CodeIntelligenceSemanticRelation.BaseType, type.BaseType,
                            session.RootPath);
                    foreach (INamedTypeSymbol contract in type.Interfaces)
                        AddSymbol(items, CodeIntelligenceSemanticRelation.BaseType, contract,
                            session.RootPath);
                    IEnumerable<INamedTypeSymbol> derived = type.TypeKind is TypeKind.Interface
                        ? await SymbolFinder.FindDerivedInterfacesAsync(type, solution,
                            cancellationToken: cancellationToken)
                        : await SymbolFinder.FindDerivedClassesAsync(type, solution,
                            cancellationToken: cancellationToken);
                    foreach (INamedTypeSymbol child in derived)
                        AddSymbol(items, CodeIntelligenceSemanticRelation.DerivedType, child,
                            session.RootPath);
                    IEnumerable<ISymbol> overrides = symbol is IMethodSymbol or IPropertySymbol or IEventSymbol
                        ? await SymbolFinder.FindOverridesAsync(symbol, solution,
                            cancellationToken: cancellationToken) : [];
                    foreach (ISymbol item in overrides)
                        AddSymbol(items, CodeIntelligenceSemanticRelation.Override, item,
                            session.RootPath);
                }
                else
                {
                    IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(
                        symbol, solution, cancellationToken);
                    foreach (ReferenceLocation reference in references.SelectMany(item => item.Locations))
                    {
                        Document? document = solution.GetDocument(reference.Location.SourceTree);
                        if (document is null || !IsTestDocument(document, reference.Location, cancellationToken))
                            continue;
                        items.Add(new(CodeIntelligenceSemanticRelation.AssociatedTest,
                            new(document.Name), MapDestination(reference.Location, document.Name,
                                session.RootPath)));
                    }
                }
            }

            CodeIntelligenceSemanticItem[] distinct = items
                .DistinctBy(item => $"{item.Relation}:{item.Display.Value}:{item.Destination.Path?.Value}:{item.Destination.Range}")
                .ToArray();
            CodeIntelligenceSemanticItem[] page = distinct.Skip(query.Offset)
                .Take(query.MaximumResults).ToArray();
            bool truncated = distinct.Length > query.Offset + page.Length;
            return new(query.Snapshot.ContextId, query.Snapshot.SessionId, query.Snapshot.Path,
                query.Snapshot.BufferVersion, SessionState(session), page,
                truncated ? query.Offset + page.Length : null, truncated, session.Issues.ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return SemanticFailure(query, "semantic_query_failed", exception.Message);
        }
        finally { session.OperationGate.Release(); }
    }

    private static void AddSymbol(
        ICollection<CodeIntelligenceSemanticItem> items,
        CodeIntelligenceSemanticRelation relation,
        ISymbol symbol,
        string root)
    {
        string display = Bound(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            MaximumIssueLength);
        Location? location = symbol.Locations.FirstOrDefault(item => item.IsInSource);
        CodeIntelligenceSymbolDestination destination = location is null
            ? new(CodeIntelligenceDestinationKind.Metadata, new(display), null, null)
            : MapDestination(location, display, root);
        items.Add(new(relation, new(display), destination));
    }

    private static bool IsTestDocument(
        Document document, Location location, CancellationToken cancellationToken)
    {
        if (document.Project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            document.FilePath?.Contains("/test", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        SyntaxNode? root = location.SourceTree?.GetRoot(cancellationToken);
        MethodDeclarationSyntax? method = root?.FindNode(location.SourceSpan)
            .AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return method?.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
            attribute.Name.ToString() is "Fact" or "Theory" or "Test" or "TestCase" or
                "FactAttribute" or "TheoryAttribute" or "TestAttribute" or "TestCaseAttribute") == true;
    }

    private static CodeIntelligenceSemanticResult SemanticFailure(
        CodeIntelligenceSemanticQuery query, string code, string error,
        CodeIntelligenceResultState state = CodeIntelligenceResultState.Failed) => new(
        query.Snapshot.ContextId, query.Snapshot.SessionId, query.Snapshot.Path,
        query.Snapshot.BufferVersion, state, [], null, false,
        [new(new(code), new(Bound(error, MaximumIssueLength)))]);

    private enum SemanticKind { Symbols, Calls, Types, Tests }

    private async ValueTask<CodeIntelligenceNavigationResult> NavigateAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        NavigationKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return NavigationFailure(snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return NavigationFailure(snapshot, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            ISymbol? symbol = await SymbolFinder.FindSymbolAtPositionAsync(
                prepared.Document!, prepared.Offset, cancellationToken);
            if (symbol is null)
            {
                return new(
                    snapshot.ContextId,
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    SessionState(session),
                    [UnavailableDestination("No symbol is available at the active caret.")],
                    session.Issues.ToArray());
            }

            IReadOnlyList<CodeIntelligenceSymbolDestination> destinations;
            if (kind is NavigationKind.References)
            {
                IEnumerable<ReferencedSymbol> found = await SymbolFinder.FindReferencesAsync(
                    symbol, prepared.Document!.Project.Solution, cancellationToken);
                IReadOnlyList<Location> locations = found
                    .SelectMany(item => item.Locations)
                    .Select(item => item.Location)
                    .Take(MaximumNavigationItems)
                    .ToArray();
                destinations = locations.Select(location => MapDestination(
                        location,
                        symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        session.RootPath))
                    .ToArray();
            }
            else if (kind is NavigationKind.Implementations)
            {
                IEnumerable<ISymbol> found = await SymbolFinder.FindImplementationsAsync(
                    symbol, prepared.Document!.Project.Solution, cancellationToken: cancellationToken);
                IEnumerable<ISymbol> overrides = symbol is IMethodSymbol or IPropertySymbol or IEventSymbol
                    ? await SymbolFinder.FindOverridesAsync(
                        symbol,
                        prepared.Document.Project.Solution,
                        cancellationToken: cancellationToken)
                    : [];
                destinations = found.Concat(overrides)
                    .Distinct(SymbolEqualityComparer.Default)
                    .SelectMany(implementation => implementation.Locations.Select(location =>
                        MapDestination(
                            location,
                            implementation.ToDisplayString(
                                SymbolDisplayFormat.MinimallyQualifiedFormat),
                            session.RootPath)))
                    .Take(MaximumNavigationItems)
                    .ToArray();
            }
            else
            {
                destinations = symbol.OriginalDefinition.Locations
                    .Take(MaximumNavigationItems)
                    .Select(location => MapDestination(
                        location,
                        symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        session.RootPath))
                    .ToArray();
            }
            if (destinations.Count == 0)
            {
                destinations = kind is NavigationKind.Implementations
                    ? [UnavailableDestination("No source implementation is available for this symbol.")]
                    : [new(
                        CodeIntelligenceDestinationKind.Metadata,
                        new(Bound(symbol.ToDisplayString(), MaximumIssueLength)),
                        null,
                        null)];
            }

            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                destinations,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return NavigationFailure(snapshot, CodeIntelligenceResultState.Failed,
                "navigation_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    private enum NavigationKind
    {
        Definition,
        References,
        Implementations,
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

    private ActiveSession? MatchingSession(CodeIntelligenceInteractiveSnapshot snapshot)
    {
        ActiveSession? session = activeSession;
        return session is not null && session.SessionId == snapshot.SessionId &&
            session.ContextId == snapshot.ContextId
            ? session
            : null;
    }

    private async ValueTask<PreparedInteractive> PrepareInteractiveAsync(
        ActiveSession session,
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (activeSession != session)
        {
            return PreparedInteractive.Failure(
                CodeIntelligenceResultState.Stale,
                Issue("session_replaced", "The Roslyn session was replaced while work was queued."));
        }

        if (!TryResolveDocumentPath(session.RootPath, snapshot.Path.Value, out string path))
        {
            return PreparedInteractive.Failure(
                CodeIntelligenceResultState.Failed,
                Issue("invalid_document_path", "The document path is outside the source context."));
        }

        Document? document = session.CurrentSolution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(candidate => candidate.FilePath is not null &&
                Path.GetFullPath(candidate.FilePath).Equals(path, StringComparison.Ordinal));
        if (document is null)
        {
            return PreparedInteractive.Failure(
                CodeIntelligenceResultState.Degraded,
                Issue("document_not_in_workspace",
                    "The document is not represented by the loaded .NET workspace."));
        }

        CodeIntelligenceIssue? baselineIssue = await VerifyBaselineAsync(
            path, snapshot.BaselineHash, cancellationToken);
        if (baselineIssue is not null)
        {
            return PreparedInteractive.Failure(
                CodeIntelligenceResultState.Stale,
                baselineIssue);
        }

        SourceText text = SourceText.From(snapshot.Text.Value, Utf8WithoutBom);
        if (snapshot.Position.Line < 0 || snapshot.Position.Character < 0 ||
            snapshot.Position.Line >= text.Lines.Count ||
            snapshot.Position.Character > text.Lines[snapshot.Position.Line].Span.Length)
        {
            return PreparedInteractive.Failure(
                CodeIntelligenceResultState.Failed,
                Issue("invalid_position", "The caret is outside the active document buffer."));
        }

        Solution candidate = session.CurrentSolution.WithDocumentText(
            document.Id, text, PreservationMode.PreserveIdentity);
        session.CurrentSolution = candidate;
        Document preparedDocument = candidate.GetDocument(document.Id)!;
        int offset = text.Lines.GetPosition(new LinePosition(
            snapshot.Position.Line, snapshot.Position.Character));
        return new(
            preparedDocument,
            text,
            offset,
            SessionState(session),
            Issue: null);
    }

    private static CodeIntelligenceResultState SessionState(ActiveSession session) =>
        session.Issues.IsEmpty
            ? CodeIntelligenceResultState.Ready
            : CodeIntelligenceResultState.Degraded;

    private static CodeIntelligenceRange Range(SourceText text, TextSpan span)
    {
        LinePositionSpan lines = text.Lines.GetLinePositionSpan(span);
        return new(
            new(lines.Start.Line, lines.Start.Character),
            new(lines.End.Line, lines.End.Character));
    }

    private static CodeIntelligencePosition Position(SourceText text, int offset)
    {
        LinePosition position = text.Lines.GetLinePosition(Math.Clamp(offset, 0, text.Length));
        return new(position.Line, position.Character);
    }

    private static CodeIntelligenceSymbolKind MapSymbolKind(ImmutableArray<string> tags)
    {
        if (tags.Contains("Keyword")) return CodeIntelligenceSymbolKind.Keyword;
        if (tags.Contains("Namespace")) return CodeIntelligenceSymbolKind.Namespace;
        if (tags.Contains("Class")) return CodeIntelligenceSymbolKind.Class;
        if (tags.Contains("Interface")) return CodeIntelligenceSymbolKind.Interface;
        if (tags.Contains("Structure")) return CodeIntelligenceSymbolKind.Structure;
        if (tags.Contains("Enum")) return CodeIntelligenceSymbolKind.Enumeration;
        if (tags.Contains("Delegate")) return CodeIntelligenceSymbolKind.Delegate;
        if (tags.Contains("ExtensionMethod")) return CodeIntelligenceSymbolKind.ExtensionMethod;
        if (tags.Contains("Method")) return CodeIntelligenceSymbolKind.Method;
        if (tags.Contains("Property")) return CodeIntelligenceSymbolKind.Property;
        if (tags.Contains("Field")) return CodeIntelligenceSymbolKind.Field;
        if (tags.Contains("Event")) return CodeIntelligenceSymbolKind.Event;
        if (tags.Contains("Constant")) return CodeIntelligenceSymbolKind.Constant;
        if (tags.Contains("Local")) return CodeIntelligenceSymbolKind.Local;
        if (tags.Contains("Parameter")) return CodeIntelligenceSymbolKind.Parameter;
        if (tags.Contains("TypeParameter")) return CodeIntelligenceSymbolKind.TypeParameter;
        if (tags.Contains("Snippet")) return CodeIntelligenceSymbolKind.Snippet;
        return CodeIntelligenceSymbolKind.Other;
    }

    private static IReadOnlyList<char> CommitCharacters(CompletionItemRules rules)
    {
        HashSet<char> characters =
        [
            ' ', '(', ')', '[', ']', '{', '}', ':', ';', ',', '.', '+', '-', '*', '/', '%',
            '&', '|', '^', '!', '~', '=', '<', '>', '?', '@', '#', '\'', '"', '\\',
        ];
        foreach (CharacterSetModificationRule rule in rules.CommitCharacterRules)
        {
            switch (rule.Kind)
            {
                case CharacterSetModificationKind.Add:
                    characters.UnionWith(rule.Characters);
                    break;
                case CharacterSetModificationKind.Remove:
                    characters.ExceptWith(rule.Characters);
                    break;
                case CharacterSetModificationKind.Replace:
                    characters.Clear();
                    characters.UnionWith(rule.Characters);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rules));
            }
        }

        return characters.Order().ToArray();
    }

    private static CodeIntelligenceSignatureItem MapSignature(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        SignatureDocumentation documentation = Documentation(method, cancellationToken);
        return new(
            new(Bound(
                method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                MaximumIssueLength)),
            new(documentation.Summary),
            method.Parameters.Select(parameter => new CodeIntelligenceSignatureParameter(
                new(parameter.Name),
                new(Bound(parameter.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    MaximumIssueLength)),
                new(documentation.Parameters.GetValueOrDefault(parameter.Name, string.Empty))))
                .ToArray());
    }

    private static SignatureDocumentation Documentation(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        string? xml = method.GetDocumentationCommentXml(
            expandIncludes: false,
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return new(string.Empty, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        try
        {
            XDocument document = XDocument.Parse(xml, LoadOptions.None);
            string summary = NormalizeDocumentation(document.Root?.Element("summary")?.Value);
            Dictionary<string, string> parameters = document.Root?.Elements("param")
                .Where(element => element.Attribute("name")?.Value is { Length: > 0 })
                .GroupBy(element => element.Attribute("name")!.Value, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => NormalizeDocumentation(group.First().Value),
                    StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            return new(summary, parameters);
        }
        catch (XmlException)
        {
            return new(string.Empty, new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    private static string NormalizeDocumentation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Bound(string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            MaximumIssueLength);
    }

    private static CodeIntelligenceSymbolDestination MapDestination(
        Location location,
        string display,
        string root)
    {
        if (!location.IsInSource || location.SourceTree?.FilePath is not { } sourcePath)
        {
            return new(
                CodeIntelligenceDestinationKind.Metadata,
                new(Bound(display, MaximumIssueLength)),
                null,
                null);
        }

        string fullPath = Path.GetFullPath(sourcePath);
        string relative = Path.GetRelativePath(root, fullPath);
        bool confined = relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        FileLinePositionSpan span = location.GetLineSpan();
        return new(
            confined && File.Exists(fullPath)
                ? CodeIntelligenceDestinationKind.Source
                : CodeIntelligenceDestinationKind.Generated,
            new(Bound(display, MaximumIssueLength)),
            confined ? new(relative) : null,
            new(
                new(span.StartLinePosition.Line, span.StartLinePosition.Character),
                new(span.EndLinePosition.Line, span.EndLinePosition.Character)));
    }

    private static CodeIntelligenceSymbolDestination UnavailableDestination(string message) => new(
        CodeIntelligenceDestinationKind.Unavailable,
        new(message),
        null,
        null);

    private static async ValueTask<IReadOnlyList<CollectedDiagnostic>> CollectDiagnosticsAsync(
        Solution solution,
        IReadOnlySet<ProjectId> projectIds,
        string root,
        CancellationToken cancellationToken)
    {
        List<CollectedDiagnostic> result = [];
        foreach (ProjectId projectId in projectIds.OrderBy(id => id.Id))
        {
            Project? project = solution.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                throw new InvalidOperationException(
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
            CodeIntelligenceDocumentPath fallback = new(project.FilePath is null
                ? project.Name
                : Path.GetRelativePath(root, project.FilePath));
            result.AddRange(diagnostics
                .Take(MaximumDiagnostics)
                .Select(diagnostic => new CollectedDiagnostic(
                    projectId,
                    MapDiagnostic(diagnostic, project.Name, root, fallback))));
        }

        return result
            .OrderByDescending(item => item.Diagnostic.Severity)
            .Take(MaximumDiagnostics)
            .ToArray();
    }

    private static IReadOnlyList<CodeIntelligenceValidationDiagnostic> CompareDiagnostics(
        IReadOnlyList<CollectedDiagnostic> baseline,
        IReadOnlyList<CollectedDiagnostic> candidate)
    {
        Dictionary<DiagnosticIdentity, Queue<CollectedDiagnostic>> remaining = baseline
            .GroupBy(Identity)
            .ToDictionary(group => group.Key, group => new Queue<CollectedDiagnostic>(group));
        List<CodeIntelligenceValidationDiagnostic> result = [];
        foreach (CollectedDiagnostic item in candidate)
        {
            DiagnosticIdentity identity = Identity(item);
            bool retained = remaining.TryGetValue(identity, out Queue<CollectedDiagnostic>? matches) &&
                matches.Count > 0;
            if (retained)
            {
                _ = matches!.Dequeue();
            }

            result.Add(new(
                retained
                    ? CodeIntelligenceDiagnosticDeltaKind.Retained
                    : CodeIntelligenceDiagnosticDeltaKind.Introduced,
                item.Diagnostic));
        }

        result.AddRange(remaining.Values
            .SelectMany(matches => matches)
            .Select(item => new CodeIntelligenceValidationDiagnostic(
                CodeIntelligenceDiagnosticDeltaKind.Resolved,
                item.Diagnostic)));
        return result;
    }

    private static DiagnosticIdentity Identity(CollectedDiagnostic diagnostic) => new(
        diagnostic.ProjectId,
        diagnostic.Diagnostic.Id.Value,
        diagnostic.Diagnostic.Message.Value,
        diagnostic.Diagnostic.Source.Value,
        diagnostic.Diagnostic.Path.Value,
        diagnostic.Diagnostic.Severity);

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

    private static CodeIntelligenceValidationResult ValidationFailure(
        CodeIntelligenceValidationRequest request,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        request.ContextId,
        request.SessionId,
        state,
        CodeIntelligenceValidationDisposition.Rejected,
        [],
        [Issue(code, message)]);

    private static CodeIntelligenceCompletionResult CompletionFailure(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        null,
        new(snapshot.Position, snapshot.Position),
        [],
        [Issue(code, message)]);

    private static CodeIntelligenceCompletionCommitResult CommitFailure(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        [],
        null,
        [Issue(code, message)]);

    private static CodeIntelligenceQuickInfoResult QuickInfoFailure(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        null,
        [],
        [Issue(code, message)]);

    private static CodeIntelligenceSignatureHelpResult SignatureFailure(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        [],
        0,
        0,
        [Issue(code, message)]);

    private static CodeIntelligenceNavigationResult NavigationFailure(
        CodeIntelligenceInteractiveSnapshot snapshot,
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

    private static string Hash(string content) => Convert.ToHexStringLower(
        SHA256.HashData(Utf8WithoutBom.GetBytes(content)));

    private sealed record CollectedDiagnostic(
        ProjectId ProjectId,
        CodeIntelligenceDiagnostic Diagnostic);

    private sealed record DiagnosticIdentity(
        ProjectId ProjectId,
        string Id,
        string Message,
        string Source,
        string Path,
        CodeIntelligenceDiagnosticSeverity Severity);

    private sealed record PreparedInteractive(
        Document? Document,
        SourceText? Text,
        int Offset,
        CodeIntelligenceResultState State,
        CodeIntelligenceIssue? Issue)
    {
        internal static PreparedInteractive Failure(
            CodeIntelligenceResultState state,
            CodeIntelligenceIssue issue) => new(null, null, 0, state, issue);
    }

    private sealed record CompletionCache(
        CodeIntelligenceCompletionListId ListId,
        CodeIntelligenceDocumentPath Path,
        CodeIntelligenceBufferVersion BufferVersion,
        string TextHash,
        CompletionService Service,
        IReadOnlyDictionary<CodeIntelligenceCompletionItemId, CompletionItem> Items);

    private sealed record SignatureDocumentation(
        string Summary,
        IReadOnlyDictionary<string, string> Parameters);

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
        internal CompletionCache? CompletionCache { get; set; }
        internal Solution PersistedSolution { get; set; } = solution;
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
