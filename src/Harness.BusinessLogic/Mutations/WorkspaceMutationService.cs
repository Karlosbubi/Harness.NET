using Harness.DataAccess.Goals;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Mutations;

internal sealed class WorkspaceMutationService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IWorkspaceFileEditor fileEditor) : IWorkspaceMutationService
{
    public async ValueTask<FileEditView> ApplyFileEditAsync(
        FileEditRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationId) || request.CorrelationId.Length > 128)
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

        WorkspaceFileEditResult result = await fileEditor.ApplyAsync(
            worktree.Path,
            new(request.Path, request.ExpectedSha256, request.Content),
            cancellationToken);
        return new(
            goal.Id,
            request.CorrelationId,
            result.Path,
            result.PreviousSha256,
            result.NewSha256,
            result.BytesWritten,
            result.WasCreated,
            result.ErrorCode,
            result.Error);
    }

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
}
