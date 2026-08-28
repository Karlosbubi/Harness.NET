using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.DataAccess.Approvals;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Tools;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Mutations;

internal sealed partial class WorkspaceMutationService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IWorkspaceFileEditor fileEditor,
    IDotNetToolRunner dotNetToolRunner,
    IToolEvidenceStore evidenceStore,
    ICapabilityApprovalStore approvalStore,
    IWorkbenchCodeIntelligenceService? codeIntelligenceService = null) : IWorkspaceMutationService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly HashSet<string> CompilerInputExtensions = new(
        [".cs", ".csproj", ".sln", ".slnx", ".props", ".targets"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly Regex IncompleteModelEditPattern = new(
        @"(?ix)
        \b(?:TODO|FIXME)\b |
        \bplaceholder\b |
        \badd\s+(?:the|your)\b[^\r\n]{0,80}\b(?:logic|implementation|checks?|code)\b |
        \bomitted\s+for\s+brevity\b |
        \bnot\s+implemented\b |
        \bNotImplementedException\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public async ValueTask<FileEditView> ApplyFileEditAsync(
        FileEditRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CorrelationId is null ||
            string.IsNullOrWhiteSpace(request.CorrelationId.Value) ||
            request.CorrelationId.Value.Length > 128)
        {
            return Failure(request, "invalid_correlation", "A correlation identifier of at most 128 characters is required.");
        }

        if (!Enum.IsDefined(request.Origin))
        {
            return Failure(request, "invalid_origin", "The edit origin must be Human or Model.");
        }

        StoredGoal? goal = await goalStore.GetAsync(request.GoalId, cancellationToken);
        StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(request.GoalId, cancellationToken);
        if (goal?.State != "Approved" || worktree?.State != "Active")
        {
            return Failure(request, "goal_not_approved", "The goal has no active approved worktree grant.");
        }

        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return Failure(request, "workspace_not_active", "The goal workspace must remain active.");
        }

        if (!workspace.IsTrusted || !worktree.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
        {
            return Failure(request, "workspace_not_trusted", "The goal workspace must remain trusted.");
        }

        StoredToolCallStart started = await StartEvidenceAsync(
            goal.Id,
            request.CorrelationId,
            ToolKind.FileEdit,
            request,
            cancellationToken);
        if (!started.WasCreated)
        {
            return Failure(request, "duplicate_correlation", "This goal already has a tool call with that correlation identifier.");
        }

        WorkbenchCodeValidationView? validation = null;
        WorkbenchCodeSessionId? validationSession = null;
        string acceptedContent = request.Content;
        IReadOnlyList<FileEditDeterministicRepairView> deterministicRepairs = [];
        bool requiresCompilerValidation = request.Origin is FileEditOrigin.Model &&
            CompilerInputExtensions.Contains(Path.GetExtension(request.Path));
        if (request.Origin is FileEditOrigin.Model)
        {
            if (requiresCompilerValidation)
            {
                Match incompleteMarker = IncompleteModelEditPattern.Match(request.Content);
                if (incompleteMarker.Success)
                {
                    FileEditView incomplete = Failure(
                        request,
                        "incomplete_model_edit_rejected",
                        $"Model-authored compiler input contains an explicit incomplete implementation marker: '{incompleteMarker.Value}'. Submit complete production code without TODOs, placeholders, omitted logic, or NotImplementedException.");
                    await CompleteEvidenceAsync(
                        started.ToolCall.Id,
                        ToolCallState.Failed,
                        incomplete);
                    return incomplete;
                }

                if (codeIntelligenceService is null)
                {
                    FileEditView unavailable = Failure(
                        request,
                        "code_intelligence_unavailable",
                        "Compiler validation is unavailable.");
                    await CompleteEvidenceAsync(
                        started.ToolCall.Id,
                        ToolCallState.Failed,
                        unavailable);
                    return unavailable;
                }

                if (request.ExpectedSha256 is null)
                {
                    FileEditView missingBaseline = Failure(
                        request,
                        "compiler_baseline_required",
                        "Model-authored compiler inputs require an exact existing-file baseline.");
                    await CompleteEvidenceAsync(
                        started.ToolCall.Id,
                        ToolCallState.Failed,
                        missingBaseline);
                    return missingBaseline;
                }

                string entryPoint = Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint);
                WorkbenchCodeSessionView session = await codeIntelligenceService.StartAsync(
                    new(new(workspace.Id), new(goal.Id), new(entryPoint)),
                    progress: null,
                    cancellationToken);
                validationSession = session.SessionId;
                if (validationSession is null ||
                    session.State is WorkbenchCodeResultState.Failed or
                        WorkbenchCodeResultState.Cancelled or WorkbenchCodeResultState.Stale)
                {
                    FileEditView unavailable = new(
                        goal.Id,
                        request.CorrelationId,
                        request.Path,
                        null,
                        null,
                        0,
                        WasCreated: false,
                        "code_intelligence_unavailable",
                        session.Issues.FirstOrDefault()?.Message.Value ??
                            "Compiler validation is unavailable.");
                    await CompleteEvidenceAsync(
                        started.ToolCall.Id,
                        ToolCallState.Failed,
                        unavailable);
                    return unavailable;
                }

                validation = await codeIntelligenceService.ValidateAsync(
                    new(
                        validationSession,
                        WorkbenchCodeValidationPhase.Candidate,
                        [new(new(request.Path), new(request.ExpectedSha256), new(request.Content))]),
                    cancellationToken);
                CandidateRepairOutcome repaired = await TryRepairCandidateAsync(
                    validationSession,
                    new(request.Path),
                    new(request.ExpectedSha256),
                    new(request.Content),
                    validation,
                    cancellationToken);
                acceptedContent = repaired.Content.Value;
                validation = repaired.Validation;
                deterministicRepairs = repaired.Repairs;
                if (!IsWarningFreeModelEdit(validation))
                {
                    FileEditView rejected = new(
                        goal.Id,
                        request.CorrelationId,
                        request.Path,
                        null,
                        null,
                        0,
                        WasCreated: false,
                        "compiler_validation_rejected",
                        validation.Issues.FirstOrDefault()?.Message.Value ??
                            "The candidate introduced a compiler warning or error.",
                        validation)
                    {
                        DeterministicRepairs = deterministicRepairs,
                    };
                    await CompleteEvidenceAsync(
                        started.ToolCall.Id,
                        ToolCallState.Failed,
                        rejected);
                    return rejected;
                }
            }
            else
            {
                validation = NotApplicableValidation(request.Path);
            }
        }

        WorkspaceFileEditResult result = await fileEditor.ApplyAsync(
            worktree.Path,
            new(request.Path, request.ExpectedSha256, acceptedContent),
            cancellationToken);
        FileEditView view = new(
            goal.Id,
            request.CorrelationId,
            result.Path,
            result.PreviousSha256,
            result.NewSha256,
            result.BytesWritten,
            result.WasCreated,
            result.ErrorCode,
            result.Error,
            validation)
        {
            DeterministicRepairs = deterministicRepairs,
        };

        if (result.ErrorCode is null && requiresCompilerValidation &&
            codeIntelligenceService is not null && validationSession is not null &&
            result.NewSha256 is not null)
        {
            WorkbenchCodeValidationView applied = await codeIntelligenceService.ValidateAsync(
                new(
                    validationSession,
                    WorkbenchCodeValidationPhase.Applied,
                    [new(new(result.Path), new(result.NewSha256), new(acceptedContent))]),
                cancellationToken);
            view = view with { AppliedCodeValidation = applied };
            if (!IsWarningFreeModelEdit(applied))
            {
                view = view with
                {
                    ErrorCode = "post_apply_validation_failed",
                    Error = applied.Issues.FirstOrDefault()?.Message.Value ??
                        "The applied edit did not match its compiler-validated candidate.",
                };
            }
        }
        await CompleteEvidenceAsync(
            started.ToolCall.Id,
            view.ErrorCode is null ? ToolCallState.Succeeded : ToolCallState.Failed,
            view);
        return view;
    }

    private static bool IsWarningFreeModelEdit(WorkbenchCodeValidationView validation) =>
        validation.Disposition is WorkbenchCodeValidationDisposition.Validated &&
        !validation.Diagnostics.Any(item =>
            item.Kind is WorkbenchCodeDiagnosticDeltaKind.Introduced &&
            item.Diagnostic.Severity is
                WorkbenchCodeDiagnosticSeverity.Warning or
                WorkbenchCodeDiagnosticSeverity.Error);

    public async ValueTask<RenameSymbolPreviewView> PreviewRenameAsync(
        RenameSymbolPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        RenameContext context = await PrepareRenameContextAsync(request, cancellationToken);
        if (context.ErrorCode is not null)
        {
            return new(null, context.ErrorCode, context.Error);
        }

        try
        {
            WorkbenchCodeRenamePreviewView preview = await codeIntelligenceService!.PreviewRenameAsync(
                new(
                    new(
                        context.SessionId!,
                        request.Path,
                        request.BaselineHash,
                        request.BufferVersion,
                        request.Text,
                        request.Position),
                    request.NewName),
                cancellationToken);
            string? grantError = ValidateRenameGrants(request, preview);
            return grantError is null
                ? new(preview, null, null)
                : new(preview, "task_file_area_denied", grantError);
        }
        finally
        {
            await codeIntelligenceService!.StopAsync(context.SessionId!, CancellationToken.None);
        }
    }

    public async ValueTask<RenameSymbolApplyView> ApplyRenameAsync(
        RenameSymbolApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        RenameSymbolPreviewRequest previewRequest = request.PreviewRequest;
        if (request.CorrelationId is null ||
            string.IsNullOrWhiteSpace(request.CorrelationId.Value) ||
            request.CorrelationId.Value.Length > 128 ||
            request.Fingerprint is null || !IsSha256(request.Fingerprint.Value))
        {
            return RenameFailure(request, "invalid_apply_request",
                "A correlation identifier and exact preview fingerprint are required.");
        }

        RenameContext context = await PrepareRenameContextAsync(previewRequest, cancellationToken);
        if (context.ErrorCode is not null)
        {
            return RenameFailure(request, context.ErrorCode, context.Error!);
        }

        StoredToolCallStart started = await StartEvidenceAsync(
            previewRequest.GoalId,
            request.CorrelationId,
            ToolKind.Rename,
            request,
            cancellationToken);
        if (!started.WasCreated)
        {
            await codeIntelligenceService!.StopAsync(context.SessionId!, CancellationToken.None);
            return RenameFailure(request, "duplicate_correlation",
                "This goal already has a tool call with that correlation identifier.");
        }

        RenameSymbolApplyView view;
        try
        {
            WorkbenchCodeRenamePreviewView preview = await codeIntelligenceService!.PreviewRenameAsync(
                new(
                    new(
                        context.SessionId!,
                        previewRequest.Path,
                        previewRequest.BaselineHash,
                        previewRequest.BufferVersion,
                        previewRequest.Text,
                        previewRequest.Position),
                    previewRequest.NewName),
                cancellationToken);
            if (preview.Disposition is not WorkbenchCodeTransformationDisposition.Ready ||
                preview.Fingerprint is null)
            {
                view = RenameFailure(request, "rename_not_ready",
                    preview.Issues.FirstOrDefault()?.Message.Value ??
                    preview.Conflicts.FirstOrDefault()?.Message.Value ??
                    "The rename preview is not ready to apply.", preview);
            }
            else if (!preview.Fingerprint.Value.Equals(request.Fingerprint.Value, StringComparison.Ordinal))
            {
                view = RenameFailure(request, "preview_changed",
                    "The rename preview no longer matches the accepted fingerprint.", preview);
            }
            else if (ValidateRenameGrants(previewRequest, preview) is { } grantError)
            {
                view = RenameFailure(request, "task_file_area_denied", grantError, preview);
            }
            else
            {
                WorkspaceFileBatchEditResult batch = await fileEditor.ApplyBatchAsync(
                    context.WorktreePath!,
                    new(preview.Edits.Select(edit => new WorkspaceFileEdit(
                        edit.Path.Value,
                        edit.BaselineHash.Value,
                        edit.Text.Value)).ToArray()),
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
                if (errorCode is null)
                {
                    applied = await codeIntelligenceService.ValidateAsync(new(
                        context.SessionId!,
                        WorkbenchCodeValidationPhase.Applied,
                        batch.Files.Zip(preview.Edits).Select(pair => new WorkbenchCodeCandidateEdit(
                            new(pair.First.Path),
                            new(pair.First.NewSha256!),
                            pair.Second.Text)).ToArray()), CancellationToken.None);
                    if (applied.Disposition is not WorkbenchCodeValidationDisposition.Validated)
                    {
                        errorCode = "post_apply_validation_failed";
                        error = applied.Issues.FirstOrDefault()?.Message.Value ??
                            "The applied rename did not match its compiler-validated preview.";
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
            view = RenameFailure(request, "cancelled", exception.Message) with { WasCancelled = true };
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

    private async ValueTask<RenameContext> PrepareRenameContextAsync(
        RenameSymbolPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (codeIntelligenceService is null)
        {
            return RenameContext.Failure("code_intelligence_unavailable", "Semantic rename is unavailable.");
        }

        if (!Enum.IsDefined(request.Origin) || request.Path is null || request.BaselineHash is null ||
            !IsSha256(request.BaselineHash.Value) || request.BufferVersion is null ||
            request.BufferVersion.Value <= 0 || request.Text is null || request.Position is null ||
            request.NewName is null || string.IsNullOrWhiteSpace(request.NewName.Value))
        {
            return RenameContext.Failure("invalid_rename_request",
                "Rename requires an exact source snapshot, caret, and new identifier.");
        }

        StoredGoal? goal = await goalStore.GetAsync(request.GoalId, cancellationToken);
        StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(request.GoalId, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (goal?.State != "Approved" || worktree?.State != "Active")
        {
            return RenameContext.Failure("goal_not_approved",
                "The goal has no active approved worktree grant.");
        }

        if (workspace is null || !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return RenameContext.Failure("workspace_not_active", "The goal workspace must remain active.");
        }

        if (!workspace.IsTrusted || !worktree.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
        {
            return RenameContext.Failure("workspace_not_trusted", "The goal workspace must remain trusted.");
        }

        string entryPoint = Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint);
        WorkbenchCodeSessionView session = await codeIntelligenceService.StartAsync(
            new(new(workspace.Id), new(goal.Id), new(entryPoint)),
            progress: null,
            cancellationToken);
        if (session.SessionId is null || session.State is WorkbenchCodeResultState.Failed or
            WorkbenchCodeResultState.Cancelled or WorkbenchCodeResultState.Stale)
        {
            return RenameContext.Failure("code_intelligence_unavailable",
                session.Issues.FirstOrDefault()?.Message.Value ?? "Semantic rename is unavailable.");
        }

        return new(session.SessionId, worktree.Path, null, null);
    }

    private static string? ValidateRenameGrants(
        RenameSymbolPreviewRequest request,
        WorkbenchCodeRenamePreviewView preview)
    {
        if (request.Origin is RenameSymbolOrigin.Human)
        {
            return null;
        }

        if (request.AllowedFileAreas is null || request.AllowedFileAreas.Count == 0)
        {
            return "The Implementer has no delegated file areas for this rename.";
        }

        if (request.AllowedFileAreas.Any(area => !ValidRenameArea(area.Value)))
        {
            return "The Implementer's delegated file areas are malformed.";
        }

        bool allowed = preview.Edits.All(edit => request.AllowedFileAreas.Any(area =>
        {
            string path = edit.Path.Value.Replace('\\', '/').Trim('/');
            string grant = area.Value.Replace('\\', '/').Trim('/');
            return path.Equals(grant, StringComparison.Ordinal) ||
                path.StartsWith(grant + "/", StringComparison.Ordinal);
        }));
        return allowed ? null : "The rename affects a path outside the Implementer's delegated file areas.";
    }

    private static bool ValidRenameArea(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
            Path.IsPathRooted(value) || value.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = value.Trim('/').Split('/');
        return segments.Length > 0 && segments.All(segment =>
            segment.Length > 0 && segment is not "." and not "..");
    }

    private static RenameSymbolApplyView RenameFailure(
        RenameSymbolApplyRequest request,
        string code,
        string error,
        WorkbenchCodeRenamePreviewView? preview = null) => new(
        request.PreviewRequest.GoalId,
        request.CorrelationId,
        preview,
        [],
        WasRolledBack: false,
        WasCancelled: false,
        AppliedCodeValidation: null,
        code,
        error);

    private static bool IsSha256(string value) => value.Length == 64 && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private sealed record RenameContext(
        WorkbenchCodeSessionId? SessionId,
        string? WorktreePath,
        string? ErrorCode,
        string? Error)
    {
        internal static RenameContext Failure(string code, string error) =>
            new(null, null, code, error);
    }

    public async ValueTask<DotNetOperationView> RunDotNetAsync(
        DotNetOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CorrelationId is null ||
            string.IsNullOrWhiteSpace(request.CorrelationId.Value) ||
            request.CorrelationId.Value.Length > 128)
        {
            return DotNetFailure(request, "invalid_correlation", "A correlation identifier of at most 128 characters is required.");
        }

        if (!Enum.IsDefined(request.Operation))
        {
            return DotNetFailure(request, "invalid_operation", "The operation must be Build, Test, or Restore.");
        }

        StoredGoal? goal = await goalStore.GetAsync(request.GoalId, cancellationToken);
        StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(request.GoalId, cancellationToken);
        if (goal?.State != "Approved" || worktree?.State != "Active")
        {
            return DotNetFailure(request, "goal_not_approved", "The goal has no active approved worktree grant.");
        }

        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return DotNetFailure(request, "workspace_not_active", "The goal workspace must remain active.");
        }

        if (!workspace.IsTrusted || !worktree.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
        {
            return DotNetFailure(request, "workspace_not_trusted", "The goal workspace must remain trusted.");
        }

        string entryPoint = Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint);
        if (request.Operation is DotNetOperation.Restore)
        {
            StoredCapabilityApproval? approval = await approvalStore.GetAsync(
                goal.Id,
                new Harness.DataAccess.Tools.ToolCorrelationId(request.CorrelationId.Value),
                CapabilityKind.Restore,
                cancellationToken);
            if (approval?.State is not CapabilityApprovalState.Approved ||
                !approval.Target.Equals(entryPoint, StringComparison.Ordinal))
            {
                return DotNetFailure(
                    request,
                    "restore_not_approved",
                    "This restore requires explicit approval for the same correlation and entry point.");
            }
        }

        StoredToolCallStart started = await StartEvidenceAsync(
            goal.Id,
            request.CorrelationId,
            ToToolKind(request.Operation),
            request,
            cancellationToken);
        if (!started.WasCreated)
        {
            return DotNetFailure(request, "duplicate_correlation", "This goal already has a tool call with that correlation identifier.");
        }

        DotNetToolResult result = await dotNetToolRunner.RunAsync(
            worktree.Path,
            new(ToDataAccessOperation(request.Operation), entryPoint),
            cancellationToken);
        DotNetOperationView view = new(
            goal.Id,
            request.CorrelationId,
            ToBusinessOperation(result.Operation),
            result.EntryPoint,
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.IsOutputTruncated,
            result.IsErrorTruncated,
            result.WasCancelled,
            result.DurationMilliseconds,
            result.ErrorCode,
            result.Error);
        ToolCallState state = result.WasCancelled
            ? ToolCallState.Cancelled
            : result.ErrorCode is null && result.ExitCode == 0
                ? ToolCallState.Succeeded
                : ToolCallState.Failed;
        await CompleteEvidenceAsync(
            started.ToolCall.Id,
            state,
            view);
        return view;
    }

    private async ValueTask<StoredToolCallStart> StartEvidenceAsync<TRequest>(
        string goalId,
        Tools.ToolCorrelationId correlationId,
        ToolKind tool,
        TRequest request,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await evidenceStore.StartAsync(new(
            new(Guid.NewGuid().ToString("N")),
            goalId,
            new Harness.DataAccess.Tools.ToolCorrelationId(correlationId.Value),
            tool,
            JsonSerializer.Serialize(request, JsonOptions),
            ToolCallState.Running,
            ResultJson: null,
            now,
            CompletedAt: null), cancellationToken);
    }

    private async ValueTask CompleteEvidenceAsync<TResult>(
        ToolCallId toolCallId,
        ToolCallState state,
        TResult result) =>
        await evidenceStore.CompleteAsync(
            toolCallId,
            ToolCallState.Running,
            state,
            JsonSerializer.Serialize(result, JsonOptions),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static ToolKind ToToolKind(DotNetOperation operation) => operation switch
    {
        DotNetOperation.Build => ToolKind.Build,
        DotNetOperation.Test => ToolKind.Test,
        DotNetOperation.Restore => ToolKind.Restore,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static DotNetToolOperation ToDataAccessOperation(DotNetOperation operation) =>
        operation switch
        {
            DotNetOperation.Build => DotNetToolOperation.Build,
            DotNetOperation.Test => DotNetToolOperation.Test,
            DotNetOperation.Restore => DotNetToolOperation.Restore,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static DotNetOperation ToBusinessOperation(DotNetToolOperation operation) =>
        operation switch
        {
            DotNetToolOperation.Build => DotNetOperation.Build,
            DotNetToolOperation.Test => DotNetOperation.Test,
            DotNetToolOperation.Restore => DotNetOperation.Restore,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static FileEditView Failure(FileEditRequest request, string code, string error) =>
        new(
            request.GoalId,
            request.CorrelationId,
            request.Path,
            null,
            null,
            0,
            WasCreated: false,
            code,
            error);

    private static WorkbenchCodeValidationView NotApplicableValidation(string path) => new(
        new(string.Empty),
        WorkbenchCodeResultState.Ready,
        WorkbenchCodeValidationDisposition.NotApplicable,
        [],
        [new(
            new("compiler_validation_not_applicable"),
            new($"{path} is outside the loaded compiler workspace."))]);

    private static DotNetOperationView DotNetFailure(
        DotNetOperationRequest request,
        string code,
        string error) =>
        new(
            request.GoalId,
            request.CorrelationId,
            request.Operation,
            string.Empty,
            null,
            string.Empty,
            string.Empty,
            IsOutputTruncated: false,
            IsErrorTruncated: false,
            WasCancelled: false,
            DurationMilliseconds: 0,
            code,
            error);
}
