using System.Text.Json;
using System.Text.Json.Serialization;
using Harness.DataAccess.Approvals;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Tools;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Mutations;

internal sealed class WorkspaceMutationService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IWorkspaceFileEditor fileEditor,
    IDotNetToolRunner dotNetToolRunner,
    IToolEvidenceStore evidenceStore,
    ICapabilityApprovalStore approvalStore) : IWorkspaceMutationService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

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

        WorkspaceFileEditResult result = await fileEditor.ApplyAsync(
            worktree.Path,
            new(request.Path, request.ExpectedSha256, request.Content),
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
            result.Error);
        await CompleteEvidenceAsync(
            started.ToolCall.Id,
            result.ErrorCode is null ? ToolCallState.Succeeded : ToolCallState.Failed,
            view);
        return view;
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
