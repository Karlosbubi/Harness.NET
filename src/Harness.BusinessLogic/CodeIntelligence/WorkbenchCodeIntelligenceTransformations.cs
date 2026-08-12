using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed partial class WorkbenchCodeIntelligenceService
{
    private const int MaximumRenameFiles = 100;

    public async ValueTask<WorkbenchCodeDocumentTransformationPreviewView>
        PreviewDocumentTransformationAsync(
            WorkbenchCodeDocumentTransformationPreviewRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool needsRange = request.Kind is
            WorkbenchCodeDocumentTransformationKind.FormatSelection or
            WorkbenchCodeDocumentTransformationKind.FormatPaste or
            WorkbenchCodeDocumentTransformationKind.FormatOnType;
        bool needsNamespace = request.Kind is WorkbenchCodeDocumentTransformationKind.AddMissingImport;
        bool needsTrigger = request.Kind is
            WorkbenchCodeDocumentTransformationKind.FormatPaste or
            WorkbenchCodeDocumentTransformationKind.FormatOnType;
        bool validTrigger = request.Kind switch
        {
            WorkbenchCodeDocumentTransformationKind.FormatPaste =>
                request.FormattingTrigger is WorkbenchCodeFormattingTrigger.Paste,
            WorkbenchCodeDocumentTransformationKind.FormatOnType =>
                request.FormattingTrigger is WorkbenchCodeFormattingTrigger.Semicolon or
                    WorkbenchCodeFormattingTrigger.CloseBrace or
                    WorkbenchCodeFormattingTrigger.NewLine,
            _ => request.FormattingTrigger is null,
        };
        if (!TryInteractive(request.Snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue) ||
            !Enum.IsDefined(request.Kind) || needsRange != (request.Range is not null) ||
            needsNamespace != (request.ImportNamespace is not null) ||
            needsTrigger != (request.FormattingTrigger is not null) || !validTrigger ||
            request.ImportNamespace is { Value.Length: 0 })
        {
            return DocumentTransformationFailure(request, issue ?? Issue(
                "invalid_document_transformation",
                "An exact source snapshot and valid closed transformation are required."));
        }

        CodeIntelligenceDocumentTransformationPreviewResult result;
        try
        {
            result = await engine.PreviewDocumentTransformationAsync(new(
                ToDataSnapshot(request.Snapshot, session!),
                Map(request.Kind),
                request.Range is null ? null : new(
                    new(request.Range.Start.Line, request.Range.Start.Character),
                    new(request.Range.End.Line, request.Range.End.Character)),
                request.ImportNamespace is null ? null : new(request.ImportNamespace.Value),
                request.FormattingTrigger is null ? null : Map(request.FormattingTrigger.Value)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DocumentTransformationFailure(request,
                Issue("cancelled", "Document transformation preview was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, request.Snapshot) ||
            !Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
                session!, request.Snapshot))
        {
            return DocumentTransformationFailure(request,
                Issue("stale_buffer", "A newer document buffer superseded this transformation preview."),
                WorkbenchCodeResultState.Stale);
        }

        bool malformed = result.Kind != Map(request.Kind) ||
            !string.Equals(result.ImportNamespace?.Value, request.ImportNamespace?.Value,
                StringComparison.Ordinal) ||
            (result.FormattingTrigger is null ? null : Map(result.FormattingTrigger.Value)) !=
                request.FormattingTrigger ||
            (result.Edit is not null &&
                (!IsConfinedRelativePath(result.Edit.Path.Value) ||
                 !IsSha256(result.Edit.BaselineHash.Value) || result.Edit.ReplacementCount < 0)) ||
            (result.Disposition is CodeIntelligenceTransformationDisposition.Ready &&
                (result.Fingerprint is null || !IsSha256(result.Fingerprint.Value) ||
                 result.Edit is null || result.Conflicts.Count != 0));
        if (malformed)
        {
            return DocumentTransformationFailure(request, Issue(
                "invalid_document_transformation_preview",
                "The Roslyn adapter returned malformed or unbounded transformation evidence."));
        }

        return new(
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            Map(result.State),
            Map(result.Disposition),
            request.Kind,
            result.Range is null ? null : Map(result.Range),
            result.Edit is null ? null : new(
                new(result.Edit.Path.Value),
                new(result.Edit.BaselineHash.Value),
                new(result.Edit.OriginalText.Value),
                new(result.Edit.Text.Value),
                result.Edit.ReplacementCount),
            result.Conflicts.Take(MaximumIssues).Select(conflict =>
                new WorkbenchCodeDocumentTransformationConflict(
                    Map(conflict.Kind), new(conflict.Message.Value))).ToArray(),
            result.Diagnostics.Where(item => IsValidDiagnostic(item.Diagnostic))
                .Take(MaximumDiagnostics)
                .Select(item => new WorkbenchCodeValidationDiagnostic(
                    Map(item.Kind), Map(item.Diagnostic))).ToArray(),
            result.Fingerprint is null ? null : new(result.Fingerprint.Value),
            MapIssues(result.Issues),
            result.ImportNamespace is null ? null : new(result.ImportNamespace.Value),
            result.FormattingTrigger is null ? null : Map(result.FormattingTrigger.Value));
    }

    public async ValueTask<WorkbenchCodeRenamePreviewView> PreviewRenameAsync(
        WorkbenchCodeRenamePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryInteractive(request.Snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue) ||
            request.NewName is null || string.IsNullOrWhiteSpace(request.NewName.Value) ||
            request.NewName.Value.Length > 256)
        {
            return RenameFailure(request, issue ??
                Issue("invalid_rename_request", "An exact source snapshot and new identifier are required."));
        }

        if (session!.SourceKind is not CodeIntelligenceSourceKind.ApprovedGoalWorktree)
        {
            return RenameFailure(request,
                Issue("editable_context_required", "Rename requires an approved goal worktree context."));
        }

        CodeIntelligenceRenamePreviewResult result;
        try
        {
            result = await engine.PreviewRenameAsync(new(
                ToDataSnapshot(request.Snapshot, session),
                new(request.NewName.Value)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return RenameFailure(request, Issue("cancelled", "Rename preview was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session, request.Snapshot) ||
            !Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
                session, request.Snapshot))
        {
            return RenameFailure(request,
                Issue("stale_buffer", "A newer document buffer superseded this rename preview."),
                WorkbenchCodeResultState.Stale);
        }

        bool malformed = result.Edits.Count > MaximumRenameFiles ||
            result.Edits.Select(edit => edit.Path.Value).Distinct(StringComparer.Ordinal).Count() !=
                result.Edits.Count ||
            result.Edits.Any(edit => !IsConfinedRelativePath(edit.Path.Value) ||
                !IsSha256(edit.BaselineHash.Value) || edit.ReplacementCount <= 0) ||
            (result.Disposition is CodeIntelligenceTransformationDisposition.Ready &&
                (result.Fingerprint is null || !IsSha256(result.Fingerprint.Value) ||
                 result.Symbol is null || result.Edits.Count == 0 || result.Conflicts.Count != 0));
        if (malformed)
        {
            return RenameFailure(request,
                Issue("invalid_rename_preview", "The rename adapter returned malformed or unbounded evidence."));
        }

        return new(
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            Map(result.State),
            Map(result.Disposition),
            result.Symbol is null ? null : new(result.Symbol.Value),
            request.NewName,
            result.Edits.Select(edit => new WorkbenchCodeRenameEdit(
                new(edit.Path.Value),
                new(edit.BaselineHash.Value),
                new(edit.OriginalText.Value),
                new(edit.Text.Value),
                edit.ReplacementCount)).ToArray(),
            result.Conflicts.Take(MaximumIssues).Select(conflict => new WorkbenchCodeRenameConflict(
                Map(conflict.Kind),
                new(conflict.Message.Value),
                conflict.Path is null ? null : new(conflict.Path.Value))).ToArray(),
            result.Diagnostics.Where(item => IsValidDiagnostic(item.Diagnostic))
                .Take(MaximumDiagnostics)
                .Select(item => new WorkbenchCodeValidationDiagnostic(
                    Map(item.Kind), Map(item.Diagnostic))).ToArray(),
            result.Fingerprint is null ? null : new(result.Fingerprint.Value),
            MapIssues(result.Issues));
    }

    private static WorkbenchCodeTransformationDisposition Map(
        CodeIntelligenceTransformationDisposition disposition) => disposition switch
        {
            CodeIntelligenceTransformationDisposition.Ready =>
                WorkbenchCodeTransformationDisposition.Ready,
            CodeIntelligenceTransformationDisposition.Conflicted =>
                WorkbenchCodeTransformationDisposition.Conflicted,
            CodeIntelligenceTransformationDisposition.Rejected =>
                WorkbenchCodeTransformationDisposition.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

    private static CodeIntelligenceDocumentTransformationKind Map(
        WorkbenchCodeDocumentTransformationKind kind) => kind switch
        {
            WorkbenchCodeDocumentTransformationKind.FormatDocument =>
                CodeIntelligenceDocumentTransformationKind.FormatDocument,
            WorkbenchCodeDocumentTransformationKind.FormatSelection =>
                CodeIntelligenceDocumentTransformationKind.FormatSelection,
            WorkbenchCodeDocumentTransformationKind.FormatChangedSpans =>
                CodeIntelligenceDocumentTransformationKind.FormatChangedSpans,
            WorkbenchCodeDocumentTransformationKind.FormatPaste =>
                CodeIntelligenceDocumentTransformationKind.FormatPaste,
            WorkbenchCodeDocumentTransformationKind.FormatOnType =>
                CodeIntelligenceDocumentTransformationKind.FormatOnType,
            WorkbenchCodeDocumentTransformationKind.OrganizeImports =>
                CodeIntelligenceDocumentTransformationKind.OrganizeImports,
            WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports =>
                CodeIntelligenceDocumentTransformationKind.RemoveUnusedImports,
            WorkbenchCodeDocumentTransformationKind.AddMissingImport =>
                CodeIntelligenceDocumentTransformationKind.AddMissingImport,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static CodeIntelligenceFormattingTrigger Map(
        WorkbenchCodeFormattingTrigger trigger) => trigger switch
        {
            WorkbenchCodeFormattingTrigger.Paste => CodeIntelligenceFormattingTrigger.Paste,
            WorkbenchCodeFormattingTrigger.Semicolon => CodeIntelligenceFormattingTrigger.Semicolon,
            WorkbenchCodeFormattingTrigger.CloseBrace => CodeIntelligenceFormattingTrigger.CloseBrace,
            WorkbenchCodeFormattingTrigger.NewLine => CodeIntelligenceFormattingTrigger.NewLine,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

    private static WorkbenchCodeFormattingTrigger Map(
        CodeIntelligenceFormattingTrigger trigger) => trigger switch
        {
            CodeIntelligenceFormattingTrigger.Paste => WorkbenchCodeFormattingTrigger.Paste,
            CodeIntelligenceFormattingTrigger.Semicolon => WorkbenchCodeFormattingTrigger.Semicolon,
            CodeIntelligenceFormattingTrigger.CloseBrace => WorkbenchCodeFormattingTrigger.CloseBrace,
            CodeIntelligenceFormattingTrigger.NewLine => WorkbenchCodeFormattingTrigger.NewLine,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

    private static WorkbenchCodeDocumentTransformationConflictKind Map(
        CodeIntelligenceDocumentTransformationConflictKind kind) => kind switch
        {
            CodeIntelligenceDocumentTransformationConflictKind.Semantic =>
                WorkbenchCodeDocumentTransformationConflictKind.Semantic,
            CodeIntelligenceDocumentTransformationConflictKind.Generated =>
                WorkbenchCodeDocumentTransformationConflictKind.Generated,
            CodeIntelligenceDocumentTransformationConflictKind.Uneditable =>
                WorkbenchCodeDocumentTransformationConflictKind.Uneditable,
            CodeIntelligenceDocumentTransformationConflictKind.TooLarge =>
                WorkbenchCodeDocumentTransformationConflictKind.TooLarge,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeRenameConflictKind Map(CodeIntelligenceRenameConflictKind kind) =>
        kind switch
        {
            CodeIntelligenceRenameConflictKind.Semantic => WorkbenchCodeRenameConflictKind.Semantic,
            CodeIntelligenceRenameConflictKind.Generated => WorkbenchCodeRenameConflictKind.Generated,
            CodeIntelligenceRenameConflictKind.Metadata => WorkbenchCodeRenameConflictKind.Metadata,
            CodeIntelligenceRenameConflictKind.OutsideSourceContext =>
                WorkbenchCodeRenameConflictKind.OutsideSourceContext,
            CodeIntelligenceRenameConflictKind.Uneditable => WorkbenchCodeRenameConflictKind.Uneditable,
            CodeIntelligenceRenameConflictKind.InconsistentLinkedFile =>
                WorkbenchCodeRenameConflictKind.InconsistentLinkedFile,
            CodeIntelligenceRenameConflictKind.TooManyFiles => WorkbenchCodeRenameConflictKind.TooManyFiles,
            CodeIntelligenceRenameConflictKind.TooLarge => WorkbenchCodeRenameConflictKind.TooLarge,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeRenamePreviewView RenameFailure(
        WorkbenchCodeRenamePreviewRequest request,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        request.Snapshot?.SessionId ?? new(string.Empty),
        request.Snapshot?.Path ?? new(string.Empty),
        request.Snapshot?.BufferVersion ?? new(0),
        state,
        WorkbenchCodeTransformationDisposition.Rejected,
        Symbol: null,
        request.NewName ?? new(string.Empty),
        [],
        [],
        [],
        Fingerprint: null,
        [issue]);

    public async ValueTask<WorkbenchCodeMissingImportView> GetMissingImportsAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryInteractive(snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue))
        {
            return MissingImportFailure(snapshot, issue!);
        }

        CodeIntelligenceMissingImportResult result;
        try
        {
            result = await engine.GetMissingImportsAsync(
                ToDataSnapshot(snapshot, session!), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MissingImportFailure(snapshot,
                Issue("cancelled", "Missing-import discovery was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, snapshot) ||
            !Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
                session!, snapshot))
        {
            return MissingImportFailure(snapshot,
                Issue("stale_buffer", "A newer document buffer superseded these import fixes."),
                WorkbenchCodeResultState.Stale);
        }

        bool malformed = result.Candidates.Count > MaximumInteractiveItems ||
            result.Candidates.Any(item => string.IsNullOrWhiteSpace(item.Namespace.Value) ||
                item.Namespace.Value.Length > 512 || string.IsNullOrWhiteSpace(item.Symbol.Value) ||
                item.Symbol.Value.Length > 1_024);
        if (malformed)
        {
            return MissingImportFailure(snapshot,
                Issue("invalid_missing_import_result",
                    "The Roslyn adapter returned malformed import candidates."));
        }

        return new(
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            Map(result.State),
            result.Candidates.Select(item => new WorkbenchCodeMissingImportCandidate(
                new(item.Namespace.Value), new(item.Symbol.Value), Map(item.Range))).ToArray(),
            MapIssues(result.Issues));
    }

    private static WorkbenchCodeMissingImportView MissingImportFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        [],
        [issue]);

    private static WorkbenchCodeDocumentTransformationPreviewView
        DocumentTransformationFailure(
            WorkbenchCodeDocumentTransformationPreviewRequest request,
            WorkbenchCodeIssue issue,
            WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
            request.Snapshot?.SessionId ?? new(string.Empty),
            request.Snapshot?.Path ?? new(string.Empty),
            request.Snapshot?.BufferVersion ?? new(0),
            state,
            WorkbenchCodeTransformationDisposition.Rejected,
            request.Kind,
            request.Range,
            Edit: null,
            [],
            [],
            Fingerprint: null,
            [issue],
            request.ImportNamespace);
}
