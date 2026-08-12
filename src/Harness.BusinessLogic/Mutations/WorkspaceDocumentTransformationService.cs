using Harness.BusinessLogic.CodeIntelligence;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Mutations;

internal sealed partial class WorkspaceMutationService
{
    public async ValueTask<DocumentTransformationPreviewView>
        PreviewDocumentTransformationAsync(
            DocumentTransformationPreviewRequest request,
            CancellationToken cancellationToken = default)
    {
        DocumentTransformationContext context =
            await PrepareDocumentTransformationContextAsync(request, cancellationToken);
        if (context.ErrorCode is not null)
        {
            return new(null, context.ErrorCode, context.Error);
        }

        try
        {
            WorkbenchCodeDocumentTransformationPreviewView preview =
                await codeIntelligenceService!.PreviewDocumentTransformationAsync(
                    ToWorkbenchRequest(request, context.SessionId!), cancellationToken);
            string? grantError = ValidateDocumentTransformationGrants(request, preview);
            return grantError is null
                ? new(preview, null, null)
                : new(preview, "task_file_area_denied", grantError);
        }
        finally
        {
            await codeIntelligenceService!.StopAsync(context.SessionId!, CancellationToken.None);
        }
    }

    public async ValueTask<DocumentTransformationApplyView>
        ApplyDocumentTransformationAsync(
            DocumentTransformationApplyRequest request,
            CancellationToken cancellationToken = default)
    {
        DocumentTransformationPreviewRequest previewRequest = request.PreviewRequest;
        if (request.CorrelationId is null ||
            string.IsNullOrWhiteSpace(request.CorrelationId.Value) ||
            request.CorrelationId.Value.Length > 128 ||
            request.Fingerprint is null || !IsSha256(request.Fingerprint.Value))
        {
            return DocumentTransformationFailure(request, "invalid_apply_request",
                "A correlation identifier and exact preview fingerprint are required.");
        }

        DocumentTransformationContext context =
            await PrepareDocumentTransformationContextAsync(previewRequest, cancellationToken);
        if (context.ErrorCode is not null)
        {
            return DocumentTransformationFailure(request, context.ErrorCode, context.Error!);
        }

        StoredToolCallStart started = await StartEvidenceAsync(
            previewRequest.GoalId,
            request.CorrelationId,
            ToolKind.DocumentTransformation,
            request,
            cancellationToken);
        if (!started.WasCreated)
        {
            await codeIntelligenceService!.StopAsync(context.SessionId!, CancellationToken.None);
            return DocumentTransformationFailure(request, "duplicate_correlation",
                "This goal already has a tool call with that correlation identifier.");
        }

        DocumentTransformationApplyView view;
        try
        {
            WorkbenchCodeDocumentTransformationPreviewView preview =
                await codeIntelligenceService!.PreviewDocumentTransformationAsync(
                    ToWorkbenchRequest(previewRequest, context.SessionId!), cancellationToken);
            if (preview.Disposition is not WorkbenchCodeTransformationDisposition.Ready ||
                preview.Fingerprint is null || preview.Edit is null)
            {
                view = DocumentTransformationFailure(request, "document_transformation_not_ready",
                    preview.Issues.FirstOrDefault()?.Message.Value ??
                    preview.Conflicts.FirstOrDefault()?.Message.Value ??
                    "The document transformation preview is not ready to apply.", preview);
            }
            else if (!preview.Fingerprint.Value.Equals(
                request.Fingerprint.Value, StringComparison.Ordinal))
            {
                view = DocumentTransformationFailure(request, "preview_changed",
                    "The document transformation no longer matches the accepted fingerprint.", preview);
            }
            else if (ValidateDocumentTransformationGrants(previewRequest, preview) is { } grantError)
            {
                view = DocumentTransformationFailure(request, "task_file_area_denied", grantError, preview);
            }
            else
            {
                WorkspaceFileBatchEditResult batch = await fileEditor.ApplyBatchAsync(
                    context.WorktreePath!,
                    new([new WorkspaceFileEdit(
                        preview.Edit.Path.Value,
                        preview.Edit.BaselineHash.Value,
                        preview.Edit.Text.Value)]),
                    cancellationToken);
                IReadOnlyList<FileEditView> files = batch.Files.Select(file => new FileEditView(
                    previewRequest.GoalId,
                    request.CorrelationId,
                    file.Path,
                    file.PreviousSha256,
                    file.NewSha256,
                    file.BytesWritten,
                    file.WasCreated,
                    file.ErrorCode,
                    file.Error)).ToArray();
                WorkbenchCodeValidationView? applied = null;
                string? errorCode = batch.ErrorCode;
                string? error = batch.Error;
                if (errorCode is null && batch.Files.SingleOrDefault()?.NewSha256 is { } newHash)
                {
                    applied = await codeIntelligenceService.ValidateAsync(new(
                        context.SessionId!,
                        WorkbenchCodeValidationPhase.Applied,
                        [new(
                            preview.Edit.Path,
                            new(newHash),
                            preview.Edit.Text)]), CancellationToken.None);
                    if (applied.Disposition is not WorkbenchCodeValidationDisposition.Validated)
                    {
                        errorCode = "post_apply_validation_failed";
                        error = applied.Issues.FirstOrDefault()?.Message.Value ??
                            "The applied transformation did not match its compiler-validated preview.";
                    }
                }

                view = new(
                    previewRequest.GoalId,
                    request.CorrelationId,
                    preview,
                    files,
                    batch.WasRolledBack,
                    batch.WasCancelled,
                    applied,
                    errorCode,
                    error);
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            view = DocumentTransformationFailure(request, "cancelled", exception.Message) with
            {
                WasCancelled = true,
            };
        }
        finally
        {
            await codeIntelligenceService!.StopAsync(context.SessionId!, CancellationToken.None);
        }

        await CompleteEvidenceAsync(
            started.ToolCall.Id,
            view.WasCancelled
                ? ToolCallState.Cancelled
                : view.ErrorCode is null ? ToolCallState.Succeeded : ToolCallState.Failed,
            view);
        return view;
    }

    private async ValueTask<DocumentTransformationContext>
        PrepareDocumentTransformationContextAsync(
            DocumentTransformationPreviewRequest request,
            CancellationToken cancellationToken)
    {
        if (codeIntelligenceService is null)
        {
            return DocumentTransformationContext.Failure(
                "code_intelligence_unavailable",
                "Document formatting and import organization are unavailable.");
        }

        if (!Enum.IsDefined(request.Origin) || !Enum.IsDefined(request.Kind) ||
            request.Path is null || request.BaselineHash is null ||
            !IsSha256(request.BaselineHash.Value) || request.BufferVersion is null ||
            request.BufferVersion.Value <= 0 || request.Text is null || request.Position is null ||
            (request.Kind is not WorkbenchCodeDocumentTransformationKind.ApplyCodeAction &&
             ((request.Kind is WorkbenchCodeDocumentTransformationKind.FormatSelection or
                    WorkbenchCodeDocumentTransformationKind.FormatPaste or
                    WorkbenchCodeDocumentTransformationKind.FormatOnType) !=
                (request.Range is not null))) ||
            (request.Kind is WorkbenchCodeDocumentTransformationKind.AddMissingImport) !=
            (request.ImportNamespace is not null) ||
            (request.Kind is WorkbenchCodeDocumentTransformationKind.ApplyCodeAction) !=
            (request.CodeActionId is not null) ||
            (request.Kind is WorkbenchCodeDocumentTransformationKind.ApplyCodeAction) !=
            (request.CodeActionScope is not null) ||
            (request.Kind is WorkbenchCodeDocumentTransformationKind.FormatPaste or
                WorkbenchCodeDocumentTransformationKind.FormatOnType) !=
            (request.FormattingTrigger is not null) ||
            !ValidFormattingTrigger(request.Kind, request.FormattingTrigger) ||
            request.ImportNamespace is { Value.Length: 0 } ||
            request.CodeActionId is { Value: var codeActionId } && !IsSha256(codeActionId) ||
            request.CodeActionScope is { } scope && !Enum.IsDefined(scope))
        {
            return DocumentTransformationContext.Failure(
                "invalid_document_transformation_request",
                "The transformation requires an exact source snapshot and a valid closed operation.");
        }

        StoredGoal? goal = await goalStore.GetAsync(request.GoalId, cancellationToken);
        StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(
            request.GoalId, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (goal?.State != "Approved" || worktree?.State != "Active")
        {
            return DocumentTransformationContext.Failure(
                "goal_not_approved", "The goal has no active approved worktree grant.");
        }

        if (workspace is null || !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return DocumentTransformationContext.Failure(
                "workspace_not_active", "The goal workspace must remain active.");
        }

        if (!workspace.IsTrusted || !worktree.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
        {
            return DocumentTransformationContext.Failure(
                "workspace_not_trusted", "The goal workspace must remain trusted.");
        }

        string entryPoint = Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint);
        WorkbenchCodeSessionView session = await codeIntelligenceService.StartAsync(
            new(new(workspace.Id), new(goal.Id), new(entryPoint)),
            progress: null,
            cancellationToken);
        if (session.SessionId is null || session.State is WorkbenchCodeResultState.Failed or
            WorkbenchCodeResultState.Cancelled or WorkbenchCodeResultState.Stale)
        {
            return DocumentTransformationContext.Failure(
                "code_intelligence_unavailable",
                session.Issues.FirstOrDefault()?.Message.Value ??
                    "Document formatting and import organization are unavailable.");
        }

        return new(session.SessionId, worktree.Path, null, null);
    }

    private static WorkbenchCodeDocumentTransformationPreviewRequest ToWorkbenchRequest(
        DocumentTransformationPreviewRequest request,
        WorkbenchCodeSessionId sessionId) => new(
        new(
            sessionId,
            request.Path,
            request.BaselineHash,
            request.BufferVersion,
            request.Text,
            request.Position),
        request.Kind,
        request.Range,
        request.ImportNamespace,
        request.FormattingTrigger,
        request.CodeActionId,
        request.CodeActionScope);

    private static bool ValidFormattingTrigger(
        WorkbenchCodeDocumentTransformationKind kind,
        WorkbenchCodeFormattingTrigger? trigger) => kind switch
        {
            WorkbenchCodeDocumentTransformationKind.FormatPaste =>
                trigger is WorkbenchCodeFormattingTrigger.Paste,
            WorkbenchCodeDocumentTransformationKind.FormatOnType =>
                trigger is WorkbenchCodeFormattingTrigger.Semicolon or
                    WorkbenchCodeFormattingTrigger.CloseBrace or
                    WorkbenchCodeFormattingTrigger.NewLine,
            _ => trigger is null,
        };

    private static string? ValidateDocumentTransformationGrants(
        DocumentTransformationPreviewRequest request,
        WorkbenchCodeDocumentTransformationPreviewView preview)
    {
        if (request.Origin is DocumentTransformationOrigin.Human)
        {
            return null;
        }

        if (request.AllowedFileAreas is null || request.AllowedFileAreas.Count == 0)
        {
            return "The Implementer has no delegated file areas for this transformation.";
        }

        if (request.AllowedFileAreas.Any(area => !ValidRenameArea(area.Value)))
        {
            return "The Implementer's delegated file areas are malformed.";
        }

        if (preview.Edit is null)
        {
            return null;
        }

        string path = preview.Edit.Path.Value.Replace('\\', '/').Trim('/');
        bool allowed = request.AllowedFileAreas.Any(area =>
        {
            string grant = area.Value.Replace('\\', '/').Trim('/');
            return path.Equals(grant, StringComparison.Ordinal) ||
                path.StartsWith(grant + "/", StringComparison.Ordinal);
        });
        return allowed
            ? null
            : "The transformation affects a path outside the Implementer's delegated file areas.";
    }

    private static DocumentTransformationApplyView DocumentTransformationFailure(
        DocumentTransformationApplyRequest request,
        string code,
        string error,
        WorkbenchCodeDocumentTransformationPreviewView? preview = null) => new(
        request.PreviewRequest.GoalId,
        request.CorrelationId,
        preview,
        [],
        WasRolledBack: false,
        WasCancelled: false,
        AppliedCodeValidation: null,
        code,
        error);

    private sealed record DocumentTransformationContext(
        WorkbenchCodeSessionId? SessionId,
        string? WorktreePath,
        string? ErrorCode,
        string? Error)
    {
        internal static DocumentTransformationContext Failure(string code, string error) =>
            new(null, null, code, error);
    }
}
