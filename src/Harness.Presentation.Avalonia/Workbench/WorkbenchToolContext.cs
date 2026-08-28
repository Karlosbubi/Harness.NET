using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed record WorkbenchToolContext(
    IWorkbenchInspectionService InspectionService,
    Func<AvaloniaShellState> State,
    Func<bool> IsBusy,
    Func<Func<ValueTask>, ValueTask> RunAsync,
    Func<string, GoalId?, ValueTask> OpenFileAsync,
    CancellationToken CancellationToken)
{
    internal WorkspaceView? ActiveWorkspace() =>
        State().Workspaces.Registered.FirstOrDefault(item => item.IsActive);

    internal GoalId? SelectedGoalId() => State().Goals.SelectedGoal?.Id;

    internal WorkbenchWorkspaceRequest Request(WorkspaceView workspace)
    {
        GoalView? goal = State().Goals.SelectedGoal;
        return new(
            new(workspace.Id),
            goal?.WorkspaceId == workspace.Id ? goal.Id : null);
    }
}
