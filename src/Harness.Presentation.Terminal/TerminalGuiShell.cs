using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Terminal.Gui.App;

namespace Harness.Presentation.Terminal;

internal sealed class TerminalGuiShell(
    IDashboardService dashboardService,
    IWorkspaceService workspaceService,
    IFrameworkService frameworkService,
    IGoalService goalService,
    IRemoteCostService remoteCostService,
    IWalkingSkeletonWorkflowService workflowService) : ITerminalShell
{
    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        DashboardSnapshot snapshot = await dashboardService.GetSnapshotAsync(cancellationToken);
        WorkspaceView? activeWorkspace = await workspaceService.GetActiveAsync(cancellationToken);
        IReadOnlyList<GoalView> goals = activeWorkspace is null
            ? []
            : await goalService.ListAsync(activeWorkspace.Id, cancellationToken);
        WorkflowSnapshot? workflow = await workflowService.GetLatestAsync(cancellationToken);

        using IApplication application = Application.Create();
        application.Init();
        using HarnessWindow window = new(
            application,
            dashboardService,
            workspaceService,
            frameworkService,
            goalService,
            remoteCostService,
            workflowService,
            snapshot,
            activeWorkspace,
            goals,
            workflow,
            cancellationToken);
        await application.RunAsync(window, cancellationToken);
    }
}
