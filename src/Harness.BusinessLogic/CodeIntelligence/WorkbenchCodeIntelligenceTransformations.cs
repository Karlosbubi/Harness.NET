using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed partial class WorkbenchCodeIntelligenceService
{
    private const int MaximumRenameFiles = 100;

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
}
