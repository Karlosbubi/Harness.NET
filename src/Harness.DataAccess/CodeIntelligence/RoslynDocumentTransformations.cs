using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumDocumentTransformationFiles = 100;
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

        bool requiresRange = request.Kind is
            CodeIntelligenceDocumentTransformationKind.FormatSelection or
            CodeIntelligenceDocumentTransformationKind.FormatPaste or
            CodeIntelligenceDocumentTransformationKind.FormatOnType;
        bool allowsOptionalRange = request.Kind is
            CodeIntelligenceDocumentTransformationKind.ApplyCodeAction;
        bool needsNamespace = request.Kind is CodeIntelligenceDocumentTransformationKind.AddMissingImport;
        bool needsCodeAction = request.Kind is CodeIntelligenceDocumentTransformationKind.ApplyCodeAction;
        bool needsTrigger = request.Kind is
            CodeIntelligenceDocumentTransformationKind.FormatPaste or
            CodeIntelligenceDocumentTransformationKind.FormatOnType;
        bool validTrigger = request.Kind switch
        {
            CodeIntelligenceDocumentTransformationKind.FormatPaste =>
                request.FormattingTrigger is CodeIntelligenceFormattingTrigger.Paste,
            CodeIntelligenceDocumentTransformationKind.FormatOnType =>
                request.FormattingTrigger is CodeIntelligenceFormattingTrigger.Semicolon or
                    CodeIntelligenceFormattingTrigger.CloseBrace or
                    CodeIntelligenceFormattingTrigger.NewLine,
            _ => request.FormattingTrigger is null,
        };
        if (!Enum.IsDefined(request.Kind) ||
            (!allowsOptionalRange && requiresRange != (request.Range is not null)) ||
            needsNamespace != (request.ImportNamespace is not null) ||
            needsCodeAction != (request.CodeActionId is not null) ||
            needsCodeAction != (request.CodeActionScope is not null) ||
            needsTrigger != (request.FormattingTrigger is not null) || !validTrigger ||
            request.ImportNamespace is { Value.Length: 0 } ||
            request.CodeActionId is { Value: var codeActionId } && !IsSha256(codeActionId) ||
            request.CodeActionScope is { } scope && !Enum.IsDefined(scope))
        {
            return DocumentTransformationFailure(request, CodeIntelligenceResultState.Failed,
                "invalid_document_transformation",
                "The closed transformation requires exactly the fields defined for its operation.");
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
            Solution? transformedSolution = null;
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
                case CodeIntelligenceDocumentTransformationKind.FormatChangedSpans:
                    IReadOnlyList<TextSpan> changedSpans = await ChangedFormattingSpansAsync(
                        session, baselineDocument, prepared.Text!, cancellationToken);
                    candidateDocument = changedSpans.Count == 0
                        ? baselineDocument
                        : await Formatter.FormatAsync(
                            baselineDocument, changedSpans, cancellationToken: cancellationToken);
                    break;
                case CodeIntelligenceDocumentTransformationKind.FormatPaste:
                case CodeIntelligenceDocumentTransformationKind.FormatOnType:
                    if (!TryGetTextSpan(prepared.Text!, request.Range, out TextSpan triggerSpan))
                    {
                        return DocumentTransformationFailure(request, CodeIntelligenceResultState.Failed,
                            "invalid_transformation_range", "The formatting range is outside the document.");
                    }
                    requestedSpan = triggerSpan;
                    TextSpan formattingSpan = ExpandFormattingSpan(
                        prepared.Text!, triggerSpan, request.FormattingTrigger!.Value);
                    candidateDocument = await Formatter.FormatAsync(
                        baselineDocument, formattingSpan, cancellationToken: cancellationToken);
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
                case CodeIntelligenceDocumentTransformationKind.ApplyCodeAction:
                    if (!TryGetCodeActionSpan(prepared.Text!, prepared.Offset, request.Range,
                        out TextSpan codeActionSpan))
                    {
                        return DocumentTransformationFailure(request,
                            CodeIntelligenceResultState.Failed,
                            "invalid_code_action_range",
                            "The code-action range is outside the document.");
                    }
                    transformedSolution = await ApplyClosedCodeActionAsync(
                        baselineDocument,
                        codeActionSpan,
                        request.CodeActionId!,
                        request.CodeActionScope!.Value,
                        cancellationToken);
                    if (transformedSolution is null)
                    {
                        return DocumentTransformationFailure(request,
                            CodeIntelligenceResultState.Stale,
                            "code_action_changed",
                            "The selected code action is no longer available or exceeded its closed scope.");
                    }
                    candidateDocument = transformedSolution.GetDocument(baselineDocument.Id) ??
                        baselineDocument;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            Solution baseline = baselineDocument.Project.Solution;
            Solution transformed = transformedSolution ?? candidateDocument.Project.Solution;
            IReadOnlyList<DocumentId> changedDocuments = transformedSolution is null
                ? [baselineDocument.Id]
                : transformed.GetChanges(baseline).GetProjectChanges()
                    .SelectMany(change => change.GetChangedDocuments())
                    .Distinct()
                    .ToArray();
            List<CodeIntelligenceDocumentTransformationConflict> mutableConflicts = [];
            Dictionary<string, CodeIntelligenceDocumentTransformationEdit> editsByPath =
                new(StringComparer.Ordinal);
            foreach (DocumentId documentId in changedDocuments)
            {
                Document? oldDocument = baseline.GetDocument(documentId);
                Document? newDocument = transformed.GetDocument(documentId);
                if (oldDocument?.FilePath is null || newDocument is null)
                {
                    mutableConflicts.Add(DocumentTransformationConflict(
                        CodeIntelligenceDocumentTransformationConflictKind.Generated,
                        "A generated document cannot be changed atomically."));
                    continue;
                }

                string fullPath = Path.GetFullPath(oldDocument.FilePath);
                string relative = Path.GetRelativePath(session.RootPath, fullPath).Replace('\\', '/');
                if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
                {
                    mutableConflicts.Add(DocumentTransformationConflict(
                        CodeIntelligenceDocumentTransformationConflictKind.OutsideSourceContext,
                        "A transformation target is outside the active source context."));
                    continue;
                }
                if (!File.Exists(fullPath))
                {
                    mutableConflicts.Add(DocumentTransformationConflict(
                        CodeIntelligenceDocumentTransformationConflictKind.Generated,
                        "A generated or missing source document is not editable.",
                        new(relative)));
                    continue;
                }
                if (!IsWritable(fullPath))
                {
                    mutableConflicts.Add(DocumentTransformationConflict(
                        CodeIntelligenceDocumentTransformationConflictKind.Uneditable,
                        "A source document affected by the transformation is not writable.",
                        new(relative)));
                    continue;
                }

                SourceText oldText = await oldDocument.GetTextAsync(cancellationToken);
                SourceText newText = await newDocument.GetTextAsync(cancellationToken);
                string original = oldText.ToString();
                string candidate = newText.ToString();
                string persisted = await File.ReadAllTextAsync(
                    fullPath, Utf8WithoutBom, cancellationToken);
                CodeIntelligenceDocumentTransformationEdit edit = new(
                    new(relative),
                    new(Hash(persisted)),
                    new(original),
                    new(candidate),
                    newText.GetTextChanges(oldText).Count);
                if (editsByPath.TryGetValue(relative, out var linked) &&
                    !linked.Text.Value.Equals(candidate, StringComparison.Ordinal))
                {
                    mutableConflicts.Add(DocumentTransformationConflict(
                        CodeIntelligenceDocumentTransformationConflictKind.InconsistentLinkedFile,
                        "Linked documents produced different edits for the same physical file.",
                        new(relative)));
                    continue;
                }
                editsByPath[relative] = edit;
            }

            if (editsByPath.Count > MaximumDocumentTransformationFiles)
            {
                mutableConflicts.Add(DocumentTransformationConflict(
                    CodeIntelligenceDocumentTransformationConflictKind.TooManyFiles,
                    $"The transformation affects more than {MaximumDocumentTransformationFiles} files."));
            }
            if (editsByPath.Values.Sum(edit =>
                    (long)Utf8WithoutBom.GetByteCount(edit.OriginalText.Value) +
                    Utf8WithoutBom.GetByteCount(edit.Text.Value)) >
                MaximumDocumentTransformationPreviewBytes)
            {
                mutableConflicts.Add(DocumentTransformationConflict(
                    CodeIntelligenceDocumentTransformationConflictKind.TooLarge,
                    "The complete transformation preview exceeds 10 MiB."));
            }

            IReadOnlyList<CodeIntelligenceDocumentTransformationEdit> edits = editsByPath.Values
                .OrderBy(edit => edit.Path.Value, StringComparer.Ordinal)
                .ToArray();
            HashSet<ProjectId> affectedProjects = changedDocuments
                .Select(id => id.ProjectId)
                .ToHashSet();
            IReadOnlyList<CollectedDiagnostic> baselineDiagnostics =
                await CollectDiagnosticsAsync(baseline, affectedProjects, session.RootPath, cancellationToken);
            IReadOnlyList<CollectedDiagnostic> candidateDiagnostics =
                await CollectDiagnosticsAsync(transformed, affectedProjects, session.RootPath, cancellationToken);
            IReadOnlyList<CodeIntelligenceValidationDiagnostic> delta = CompareDiagnostics(
                baselineDiagnostics, candidateDiagnostics);
            mutableConflicts.AddRange(delta
                .Where(item => item.Kind is CodeIntelligenceDiagnosticDeltaKind.Introduced &&
                    item.Diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Error)
                .Select(item => DocumentTransformationConflict(
                    CodeIntelligenceDocumentTransformationConflictKind.Semantic,
                    $"{item.Diagnostic.Id.Value}: {item.Diagnostic.Message.Value}"))
                .Take(MaximumIssues));
            IReadOnlyList<CodeIntelligenceDocumentTransformationConflict> conflicts =
                mutableConflicts.Take(MaximumIssues).ToArray();
            CodeIntelligenceTransformationDisposition disposition = conflicts.Count == 0
                ? CodeIntelligenceTransformationDisposition.Ready
                : CodeIntelligenceTransformationDisposition.Conflicted;
            CodeIntelligenceTransformationFingerprint? fingerprint = disposition is
                CodeIntelligenceTransformationDisposition.Ready
                    ? new(DocumentTransformationFingerprint(
                        snapshot, request.Kind, request.Range, request.ImportNamespace,
                        request.FormattingTrigger, request.CodeActionId,
                        request.CodeActionScope, edits, delta))
                    : null;
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                disposition,
                request.Kind,
                requestedSpan is null ? null : Range(prepared.Text!, requestedSpan.Value),
                edits,
                conflicts,
                delta.Take(MaximumDiagnostics).ToArray(),
                fingerprint,
                session.Issues.ToArray(),
                request.ImportNamespace,
                request.FormattingTrigger,
                request.CodeActionId,
                request.CodeActionScope);
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
        CodeIntelligenceFormattingTrigger? formattingTrigger,
        CodeIntelligenceCodeActionId? codeActionId,
        CodeIntelligenceCodeActionScope? codeActionScope,
        IReadOnlyList<CodeIntelligenceDocumentTransformationEdit> edits,
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
            .Append(formattingTrigger).Append('\n')
            .Append(codeActionId?.Value).Append('\n')
            .Append(codeActionScope).Append('\n');
        foreach (CodeIntelligenceDocumentTransformationEdit edit in edits)
        {
            _ = value.Append(edit.Path.Value).Append('\0')
                .Append(edit.BaselineHash.Value).Append('\0')
                .Append(Hash(edit.OriginalText.Value)).Append('\0')
                .Append(Hash(edit.Text.Value)).Append('\0')
                .Append(edit.ReplacementCount).Append('\n');
        }
        foreach (CodeIntelligenceValidationDiagnostic item in diagnostics)
        {
            _ = value.Append(item.Kind).Append('\0')
                .Append(item.Diagnostic.Id.Value).Append('\0')
                .Append(item.Diagnostic.Path.Value).Append('\0')
                .Append(item.Diagnostic.Message.Value).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static CodeIntelligenceDocumentTransformationConflict DocumentTransformationConflict(
        CodeIntelligenceDocumentTransformationConflictKind kind,
        string message,
        CodeIntelligenceDocumentPath? path = null) =>
        new(kind, new(Bound(message, MaximumIssueLength)), path);

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
            Edits: [],
            [],
            [],
            Fingerprint: null,
            [Issue(code, message)],
            request.ImportNamespace,
            request.FormattingTrigger,
            request.CodeActionId,
            request.CodeActionScope);

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
            Edits: [],
            [conflict],
            [],
            Fingerprint: null,
            session.Issues.ToArray(),
            request.ImportNamespace,
            request.FormattingTrigger,
            request.CodeActionId,
            request.CodeActionScope);

    private static async ValueTask<IReadOnlyList<TextSpan>> ChangedFormattingSpansAsync(
        ActiveSession session,
        Document currentDocument,
        SourceText currentText,
        CancellationToken cancellationToken)
    {
        Document? persistedDocument = session.PersistedSolution.GetDocument(currentDocument.Id);
        if (persistedDocument is null)
        {
            return [];
        }

        SyntaxTree? persistedTree = await persistedDocument.GetSyntaxTreeAsync(cancellationToken);
        SyntaxTree? currentTree = await currentDocument.GetSyntaxTreeAsync(cancellationToken);
        if (persistedTree is null || currentTree is null)
        {
            return [];
        }

        IList<TextChange> changes = currentTree.GetChanges(persistedTree);
        if (changes.Count == 0)
        {
            return [];
        }

        List<TextSpan> spans = [];
        int delta = 0;
        foreach (TextChange change in changes.OrderBy(item => item.Span.Start))
        {
            int start = Math.Clamp(change.Span.Start + delta, 0, currentText.Length);
            int length = change.NewText?.Length ?? 0;
            spans.Add(ExpandFormattingSpan(
                currentText,
                new TextSpan(start, Math.Min(length, currentText.Length - start)),
                CodeIntelligenceFormattingTrigger.Paste));
            delta += length - change.Span.Length;
        }

        return spans
            .OrderBy(item => item.Start)
            .Aggregate(new List<TextSpan>(), static (merged, next) =>
            {
                if (merged.Count == 0 || merged[^1].End < next.Start)
                {
                    merged.Add(next);
                }
                else
                {
                    TextSpan previous = merged[^1];
                    merged[^1] = TextSpan.FromBounds(
                        previous.Start, Math.Max(previous.End, next.End));
                }
                return merged;
            });
    }

    private static TextSpan ExpandFormattingSpan(
        SourceText text,
        TextSpan span,
        CodeIntelligenceFormattingTrigger trigger)
    {
        int startPosition = Math.Clamp(span.Start, 0, text.Length);
        int endPosition = Math.Clamp(span.End, startPosition, text.Length);
        int startLine = text.Lines.GetLineFromPosition(startPosition).LineNumber;
        int endLookup = endPosition > startPosition ? endPosition - 1 : endPosition;
        int endLine = text.Lines.GetLineFromPosition(Math.Clamp(endLookup, 0, text.Length)).LineNumber;
        if (trigger is CodeIntelligenceFormattingTrigger.NewLine && startLine > 0)
        {
            startLine--;
        }

        return TextSpan.FromBounds(
            text.Lines[startLine].Start,
            text.Lines[endLine].EndIncludingLineBreak);
    }
}
