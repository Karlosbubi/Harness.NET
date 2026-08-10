using Harness.DataAccess.Evidence;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Evidence;

internal sealed class ToolEvidenceService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IToolEvidenceStore evidenceStore) : IToolEvidenceService
{
    public async ValueTask<ToolEvidenceSnapshot> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        StoredGoal? goal = await goalStore.GetAsync(goalId, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (goal is null || workspace is null ||
            !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return new([], "workspace_not_active", "The goal workspace must be active to view its evidence.");
        }

        IReadOnlyList<StoredToolCall> stored = await evidenceStore.ListAsync(
            goal.Id,
            cancellationToken);
        return new(
            stored.Select(item => new ToolEvidenceView(
                new(item.Id.Value),
                item.GoalId,
                new(item.CorrelationId.Value),
                item.Tool switch
                {
                    DataAccess.Evidence.ToolKind.FileEdit => ToolKind.FileEdit,
                    DataAccess.Evidence.ToolKind.Rename => ToolKind.Rename,
                    DataAccess.Evidence.ToolKind.Build => ToolKind.Build,
                    DataAccess.Evidence.ToolKind.Test => ToolKind.Test,
                    DataAccess.Evidence.ToolKind.Restore => ToolKind.Restore,
                    DataAccess.Evidence.ToolKind.VisualCapture => ToolKind.VisualCapture,
                    _ => throw new InvalidOperationException("The stored tool kind is unsupported."),
                },
                item.RequestJson,
                item.State switch
                {
                    ToolCallState.Running => ToolEvidenceState.Running,
                    ToolCallState.Succeeded => ToolEvidenceState.Succeeded,
                    ToolCallState.Failed => ToolEvidenceState.Failed,
                    ToolCallState.Cancelled => ToolEvidenceState.Cancelled,
                    ToolCallState.Uncertain => ToolEvidenceState.Uncertain,
                    _ => throw new InvalidOperationException("The stored tool state is unsupported."),
                },
                item.ResultJson,
                item.StartedAt,
                item.CompletedAt)).ToArray(),
            ErrorCode: null,
            Error: null);
    }
}
