using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Mutations;

namespace Harness.BusinessLogic.Documents;

internal sealed class WorkbenchDocumentService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IWorkspaceFileReader fileReader,
    IWorkspaceMutationService mutationService,
    IWorkspaceFileEditor fileEditor) : IWorkbenchDocumentService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async ValueTask<WorkbenchDocumentView> OpenAsync(
        WorkbenchDocumentOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkspaceId is null || string.IsNullOrWhiteSpace(request.WorkspaceId.Value) ||
            request.Path is null || string.IsNullOrWhiteSpace(request.Path.Value))
        {
            return OpenFailure(
                request,
                "invalid_request",
                "A workspace and relative document path are required.");
        }

        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(request.WorkspaceId, request.GoalId),
            cancellationToken);
        if (resolution.Error is not null || resolution.RootPath is null)
        {
            return OpenFailure(
                request,
                resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The workspace context is unavailable.");
        }

        WorkbenchDocumentAccess access = resolution.Context.Scope is
            WorkbenchWorkspaceScope.ApprovedGoalWorktree or
            WorkbenchWorkspaceScope.OriginalWorkspace
            ? WorkbenchDocumentAccess.Editable
            : WorkbenchDocumentAccess.ReadOnly;
        string accessDescription = resolution.Context.Scope switch
        {
            WorkbenchWorkspaceScope.ApprovedGoalWorktree =>
                $"Editing {resolution.Context.Description}.",
            WorkbenchWorkspaceScope.OriginalWorkspace when request.GoalId is not null =>
                "Editing the active trusted workspace; the selected goal has no active worktree.",
            _ =>
                "Editing the active trusted workspace.",
        };

        WorkspaceFileRead file = await fileReader.ReadAsync(
            resolution.RootPath,
            request.Path.Value,
            cancellationToken);
        if (file.Error is not null)
        {
            return new(
                request.WorkspaceId,
                resolution.Context.GoalId,
                resolution.Context.Branch,
                new(file.Path),
                new(file.Content),
                null,
                new(file.SizeBytes),
                file.IsTruncated,
                WorkbenchDocumentAccess.ReadOnly,
                accessDescription,
                file.ErrorCode,
                file.Error);
        }

        WorkbenchDocumentSha256? hash = file.IsTruncated
            ? null
            : new(Convert.ToHexStringLower(SHA256.HashData(
                Utf8WithoutBom.GetBytes(file.Content))));
        if (file.IsTruncated)
        {
            access = WorkbenchDocumentAccess.ReadOnly;
            accessDescription =
                "Read-only because the bounded source view is truncated; the complete file was not loaded.";
        }

        return new(
            request.WorkspaceId,
            resolution.Context.GoalId,
            resolution.Context.Branch,
            new(file.Path),
            new(file.Content),
            hash,
            new(file.SizeBytes),
            file.IsTruncated,
            access,
            accessDescription,
            ErrorCode: null,
            Error: null);
    }

    public async ValueTask<WorkbenchDocumentSaveResult> SaveAsync(
        WorkbenchDocumentSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkspaceId is null || string.IsNullOrWhiteSpace(request.WorkspaceId.Value) ||
            (request.GoalId is not null && string.IsNullOrWhiteSpace(request.GoalId.Value)) ||
            request.CorrelationId is null || string.IsNullOrWhiteSpace(request.CorrelationId.Value) ||
            request.Path is null || string.IsNullOrWhiteSpace(request.Path.Value) ||
            (request.ExpectedSha256 is not null &&
             string.IsNullOrWhiteSpace(request.ExpectedSha256.Value)) ||
            request.Content is null)
        {
            return SaveFailure(
                request,
                WorkbenchDocumentSaveOutcome.Rejected,
                "invalid_request",
                "A workspace, correlation, path, valid baseline, and document content are required.");
        }

        if (request.GoalId is null)
        {
            return await SaveOriginalWorkspaceAsync(request, cancellationToken);
        }

        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(request.WorkspaceId, request.GoalId),
            cancellationToken);
        if (resolution.Error is not null ||
            resolution.Context.Scope is not WorkbenchWorkspaceScope.ApprovedGoalWorktree ||
            resolution.Context.GoalId != request.GoalId)
        {
            return SaveFailure(
                request,
                WorkbenchDocumentSaveOutcome.Rejected,
                resolution.ErrorCode ?? "goal_not_approved",
                resolution.Error ?? "The goal has no active approved worktree grant.");
        }

        FileEditView edit = await mutationService.ApplyFileEditAsync(
            new(
                request.GoalId.Value,
                request.CorrelationId,
                request.Path.Value,
                request.ExpectedSha256?.Value,
                request.Content.Value),
            cancellationToken);
        WorkbenchDocumentSaveOutcome outcome = edit.ErrorCode switch
        {
            null => WorkbenchDocumentSaveOutcome.Saved,
            "content_changed" => WorkbenchDocumentSaveOutcome.Conflict,
            "goal_not_approved" or "workspace_not_active" or "workspace_not_trusted" =>
                WorkbenchDocumentSaveOutcome.Rejected,
            _ => WorkbenchDocumentSaveOutcome.Failed,
        };
        return new(
            request.WorkspaceId,
            request.GoalId,
            request.CorrelationId,
            new(edit.Path),
            request.ExpectedSha256,
            edit.PreviousSha256 is null ? null : new(edit.PreviousSha256),
            edit.NewSha256 is null ? null : new(edit.NewSha256),
            new(edit.BytesWritten),
            outcome,
            edit.ErrorCode,
            edit.Error);
    }

    private async ValueTask<WorkbenchDocumentSaveResult> SaveOriginalWorkspaceAsync(
        WorkbenchDocumentSaveRequest request,
        CancellationToken cancellationToken)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(request.WorkspaceId, GoalId: null),
            cancellationToken);
        if (resolution.Error is not null || resolution.RootPath is null ||
            resolution.Context.Scope is not WorkbenchWorkspaceScope.OriginalWorkspace)
        {
            return SaveFailure(
                request,
                WorkbenchDocumentSaveOutcome.Rejected,
                resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The original workspace is unavailable.");
        }

        WorkspaceFileEditResult edit = await fileEditor.ApplyAsync(
            resolution.RootPath,
            new(
                request.Path.Value,
                request.ExpectedSha256?.Value,
                request.Content.Value),
            cancellationToken);
        WorkbenchDocumentSaveOutcome outcome = edit.ErrorCode switch
        {
            null => WorkbenchDocumentSaveOutcome.Saved,
            "content_changed" => WorkbenchDocumentSaveOutcome.Conflict,
            "invalid_hash" or "invalid_path" or "outside_workspace" or
                "symlink_not_allowed" or "workspace_missing" or "parent_missing" or
                "content_too_large" or "existing_file_too_large" =>
                WorkbenchDocumentSaveOutcome.Rejected,
            _ => WorkbenchDocumentSaveOutcome.Failed,
        };
        return new(
            request.WorkspaceId,
            GoalId: null,
            request.CorrelationId,
            new(edit.Path),
            request.ExpectedSha256,
            edit.PreviousSha256 is null ? null : new(edit.PreviousSha256),
            edit.NewSha256 is null ? null : new(edit.NewSha256),
            new(edit.BytesWritten),
            outcome,
            edit.ErrorCode,
            edit.Error);
    }

    private static WorkbenchDocumentView OpenFailure(
        WorkbenchDocumentOpenRequest request,
        string code,
        string error) => new(
        request.WorkspaceId ?? new(string.Empty),
        null,
        null,
        request.Path ?? new(string.Empty),
        new(string.Empty),
        null,
        new(0),
        IsTruncated: false,
        WorkbenchDocumentAccess.ReadOnly,
        "Document unavailable.",
        code,
        error);

    private static WorkbenchDocumentSaveResult SaveFailure(
        WorkbenchDocumentSaveRequest request,
        WorkbenchDocumentSaveOutcome outcome,
        string code,
        string error) => new(
        request.WorkspaceId ?? new(string.Empty),
        request.GoalId,
        request.CorrelationId ?? new(string.Empty),
        request.Path ?? new(string.Empty),
        request.ExpectedSha256,
        null,
        null,
        new(0),
        outcome,
        code,
        error);
}
