using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Documents;

internal sealed class WorkbenchDocumentService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IWorkspaceFileReader fileReader,
    IWorkspaceMutationService mutationService) : IWorkbenchDocumentService
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
            WorkbenchWorkspaceScope.ApprovedGoalWorktree
            ? WorkbenchDocumentAccess.Editable
            : WorkbenchDocumentAccess.ReadOnly;
        string accessDescription = resolution.Context.Scope switch
        {
            WorkbenchWorkspaceScope.ApprovedGoalWorktree =>
                $"Editing {resolution.Context.Description}.",
            WorkbenchWorkspaceScope.OriginalWorkspace when request.GoalId is not null =>
                "Read-only original workspace. Approve the selected goal plan to edit safely.",
            _ =>
                "Read-only original workspace. Select an approved goal to edit in its isolated worktree.",
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
        if (request.GoalId is null || string.IsNullOrWhiteSpace(request.GoalId.Value) ||
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
                "A goal, correlation, path, valid baseline, and document content are required.");
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
        request.GoalId ?? new(string.Empty),
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
