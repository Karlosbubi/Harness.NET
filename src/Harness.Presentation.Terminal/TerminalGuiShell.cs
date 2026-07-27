using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Terminal.Gui.App;

namespace Harness.Presentation.Terminal;

internal sealed class TerminalGuiShell(
    IDashboardService dashboardService,
    IWorkspaceService workspaceService,
    IFrameworkService frameworkService,
    IWalkingSkeletonWorkflowService workflowService) : ITerminalShell
{
    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        DashboardSnapshot snapshot = await dashboardService.GetSnapshotAsync(cancellationToken);
        WorkspaceView? activeWorkspace = await workspaceService.GetActiveAsync(cancellationToken);
        WorkflowSnapshot? workflow = await workflowService.GetLatestAsync(cancellationToken);

        using IApplication application = Application.Create();
        application.Init();
        using HarnessWindow window = new(
            application,
            dashboardService,
            workspaceService,
            frameworkService,
            workflowService,
            snapshot,
            activeWorkspace,
            workflow,
            cancellationToken);
        await application.RunAsync(window, cancellationToken);
    }
}
