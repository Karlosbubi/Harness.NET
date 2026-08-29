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

internal sealed partial class RoslynCodeIntelligenceEngine
{
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
            bool analyzerFailed = diagnostics.Any(IsAnalyzerFailureDiagnostic);
            CodeIntelligenceIssue[] issues = session.Issues
                .Concat(analyzerFailed
                    ? [Issue(
                        "analyzer_failed",
                        "One or more project analyzers failed. Compiler diagnostics remain available.")]
                    : [])
                .DistinctBy(issue => issue.Code)
                .Take(MaximumIssues)
                .ToArray();
            session.CurrentSolution = candidate;
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                issues.Length == 0
                    ? CodeIntelligenceResultState.Ready
                    : CodeIntelligenceResultState.Degraded,
                diagnostics
                    .Where(diagnostic => !IsAnalyzerFailureDiagnostic(diagnostic))
                    .Where(diagnostic => IsForDocument(diagnostic, path))
                    .Take(MaximumDiagnostics)
                    .Select(diagnostic => MapDiagnostic(
                        diagnostic,
                        project.Name,
                        session.RootPath,
                        snapshot.Path))
                    .ToArray(),
                issues);
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

}
