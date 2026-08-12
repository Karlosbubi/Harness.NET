using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumDocumentTransformationPreviewBytes = 10 * 1024 * 1024;

    public async ValueTask<CodeIntelligenceDocumentTransformationPreviewResult>
        PreviewDocumentTransformationAsync(
            CodeIntelligenceDocumentTransformationPreviewRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CodeIntelligenceInteractiveSnapshot snapshot = request.Snapshot;
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return DocumentTransformationFailure(request, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        bool needsRange = request.Kind is CodeIntelligenceDocumentTransformationKind.FormatSelection;
        bool needsNamespace = request.Kind is CodeIntelligenceDocumentTransformationKind.AddMissingImport;
        if (!Enum.IsDefined(request.Kind) || needsRange != (request.Range is not null) ||
            needsNamespace != (request.ImportNamespace is not null) ||
            request.ImportNamespace is { Value.Length: 0 })
        {
            return DocumentTransformationFailure(request, CodeIntelligenceResultState.Failed,
                "invalid_document_transformation",
                "Format Selection requires one exact range and Add Missing Import requires one discovered namespace.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return DocumentTransformationFailure(request, prepared.State,
                    prepared.Issue.Code.Value, prepared.Issue.Message.Value);
            }

            Document baselineDocument = prepared.Document!;
            if (baselineDocument.FilePath is null || !File.Exists(baselineDocument.FilePath))
            {
                return DocumentTransformationConflictResult(request, session,
                    DocumentTransformationConflict(
                        CodeIntelligenceDocumentTransformationConflictKind.Generated,
                        "Generated or missing documents cannot be changed."));
            }

            if (!IsWritable(baselineDocument.FilePath))
            {
                return DocumentTransformationConflictResult(request, session,
                    DocumentTransformationConflict(
                        CodeIntelligenceDocumentTransformationConflictKind.Uneditable,
                        "The source document is not writable."));
            }

            Document candidateDocument;
            TextSpan? requestedSpan = null;
            switch (request.Kind)
            {
                case CodeIntelligenceDocumentTransformationKind.FormatDocument:
                    candidateDocument = await Formatter.FormatAsync(
                        baselineDocument, cancellationToken: cancellationToken);
                    break;
                case CodeIntelligenceDocumentTransformationKind.FormatSelection:
                    if (!TryGetTextSpan(prepared.Text!, request.Range, out TextSpan span))
                    {
                        return DocumentTransformationFailure(request, CodeIntelligenceResultState.Failed,
                            "invalid_transformation_range", "The requested selection is outside the document.");
                    }
                    requestedSpan = span;
                    candidateDocument = await Formatter.FormatAsync(
                        baselineDocument, span, cancellationToken: cancellationToken);
                    break;
                case CodeIntelligenceDocumentTransformationKind.OrganizeImports:
                    candidateDocument = await Formatter.OrganizeImportsAsync(
                        baselineDocument, cancellationToken);
                    break;
                case CodeIntelligenceDocumentTransformationKind.RemoveUnusedImports:
                    candidateDocument = await RemoveUnusedImportsAsync(
                        baselineDocument, cancellationToken);
                    break;
                case CodeIntelligenceDocumentTransformationKind.AddMissingImport:
                    IReadOnlyList<MissingImportCandidateDocument> candidates =
                        await FindMissingImportCandidatesAsync(prepared, cancellationToken);
                    MissingImportCandidateDocument? import = candidates.SingleOrDefault(item =>
                        item.Namespace.Equals(request.ImportNamespace!.Value, StringComparison.Ordinal));
                    if (import is null)
                    {
                        return DocumentTransformationFailure(request, CodeIntelligenceResultState.Failed,
                            "missing_import_candidate_changed",
                            "The selected namespace no longer resolves the unresolved type at the caret.");
                    }
                    candidateDocument = import.Document;
                    requestedSpan = import.Span;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            SourceText originalText = prepared.Text!;
            SourceText candidateText = await candidateDocument.GetTextAsync(cancellationToken);
            string original = originalText.ToString();
            string candidate = candidateText.ToString();
            int replacements = candidateText.GetTextChanges(originalText).Count;
            if ((long)Utf8WithoutBom.GetByteCount(original) +
                Utf8WithoutBom.GetByteCount(candidate) > MaximumDocumentTransformationPreviewBytes)
            {
                return DocumentTransformationConflictResult(request, session,
                    DocumentTransformationConflict(
                        CodeIntelligenceDocumentTransformationConflictKind.TooLarge,
                        "The complete transformation preview exceeds 10 MiB."));
            }

            Solution baseline = baselineDocument.Project.Solution;
            Solution transformed = candidateDocument.Project.Solution;
            HashSet<ProjectId> affectedProjects = [baselineDocument.Project.Id];
            IReadOnlyList<CollectedDiagnostic> baselineDiagnostics =
                await CollectDiagnosticsAsync(baseline, affectedProjects, session.RootPath, cancellationToken);
            IReadOnlyList<CollectedDiagnostic> candidateDiagnostics =
                await CollectDiagnosticsAsync(transformed, affectedProjects, session.RootPath, cancellationToken);
            IReadOnlyList<CodeIntelligenceValidationDiagnostic> delta = CompareDiagnostics(
                baselineDiagnostics, candidateDiagnostics);
            IReadOnlyList<CodeIntelligenceDocumentTransformationConflict> conflicts = delta
                .Where(item => item.Kind is CodeIntelligenceDiagnosticDeltaKind.Introduced &&
                    item.Diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Error)
                .Select(item => DocumentTransformationConflict(
                    CodeIntelligenceDocumentTransformationConflictKind.Semantic,
                    $"{item.Diagnostic.Id.Value}: {item.Diagnostic.Message.Value}"))
                .Take(MaximumIssues)
                .ToArray();
            CodeIntelligenceTransformationDisposition disposition = conflicts.Count == 0
                ? CodeIntelligenceTransformationDisposition.Ready
                : CodeIntelligenceTransformationDisposition.Conflicted;
            CodeIntelligenceDocumentTransformationEdit edit = new(
                snapshot.Path,
                snapshot.BaselineHash,
                new(original),
                new(candidate),
                replacements);
            CodeIntelligenceTransformationFingerprint? fingerprint = disposition is
                CodeIntelligenceTransformationDisposition.Ready
                    ? new(DocumentTransformationFingerprint(
                        snapshot, request.Kind, request.Range, request.ImportNamespace, edit, delta))
                    : null;
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                disposition,
                request.Kind,
                requestedSpan is null ? null : Range(originalText, requestedSpan.Value),
                edit,
                conflicts,
                delta.Take(MaximumDiagnostics).ToArray(),
                fingerprint,
                session.Issues.ToArray(),
                request.ImportNamespace);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException)
        {
            return DocumentTransformationFailure(request, CodeIntelligenceResultState.Failed,
                "document_transformation_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    private static string DocumentTransformationFingerprint(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CodeIntelligenceDocumentTransformationKind kind,
        CodeIntelligenceRange? range,
        CodeIntelligenceImportNamespace? importNamespace,
        CodeIntelligenceDocumentTransformationEdit edit,
        IReadOnlyList<CodeIntelligenceValidationDiagnostic> diagnostics)
    {
        StringBuilder value = new();
        _ = value.Append(snapshot.ContextId.Value).Append('\n')
            .Append(snapshot.Path.Value).Append('\n')
            .Append(snapshot.BaselineHash.Value).Append('\n')
            .Append(snapshot.BufferVersion.Value).Append('\n')
            .Append(kind).Append('\n')
            .Append(range?.Start.Line).Append(':').Append(range?.Start.Character).Append('-')
            .Append(range?.End.Line).Append(':').Append(range?.End.Character).Append('\n')
            .Append(importNamespace?.Value).Append('\n')
            .Append(Hash(edit.OriginalText.Value)).Append('\n')
            .Append(Hash(edit.Text.Value)).Append('\n')
            .Append(edit.ReplacementCount).Append('\n');
        foreach (CodeIntelligenceValidationDiagnostic item in diagnostics)
        {
            _ = value.Append(item.Kind).Append('\0')
                .Append(item.Diagnostic.Id.Value).Append('\0')
                .Append(item.Diagnostic.Path.Value).Append('\0')
                .Append(item.Diagnostic.Message.Value).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private static CodeIntelligenceDocumentTransformationConflict DocumentTransformationConflict(
        CodeIntelligenceDocumentTransformationConflictKind kind,
        string message) => new(kind, new(Bound(message, MaximumIssueLength)));

    private static CodeIntelligenceDocumentTransformationPreviewResult
        DocumentTransformationFailure(
            CodeIntelligenceDocumentTransformationPreviewRequest request,
            CodeIntelligenceResultState state,
            string code,
            string message) => new(
            request.Snapshot.ContextId,
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            state,
            CodeIntelligenceTransformationDisposition.Rejected,
            request.Kind,
            request.Range,
            Edit: null,
            [],
            [],
            Fingerprint: null,
            [Issue(code, message)],
            request.ImportNamespace);

    private static CodeIntelligenceDocumentTransformationPreviewResult
        DocumentTransformationConflictResult(
            CodeIntelligenceDocumentTransformationPreviewRequest request,
            ActiveSession session,
            CodeIntelligenceDocumentTransformationConflict conflict) => new(
            request.Snapshot.ContextId,
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            SessionState(session),
            CodeIntelligenceTransformationDisposition.Conflicted,
            request.Kind,
            request.Range,
            Edit: null,
            [conflict],
            [],
            Fingerprint: null,
            session.Issues.ToArray(),
            request.ImportNamespace);
}
