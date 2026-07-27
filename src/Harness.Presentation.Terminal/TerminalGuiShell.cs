using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Workspaces;
using Terminal.Gui.App;

namespace Harness.Presentation.Terminal;

internal sealed class TerminalGuiShell(
    IDashboardService dashboardService,
    IWorkspaceService workspaceService,
    IFrameworkService frameworkService) : ITerminalShell
{
    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        DashboardSnapshot snapshot = await dashboardService.GetSnapshotAsync(cancellationToken);
        WorkspaceView? activeWorkspace = await workspaceService.GetActiveAsync(cancellationToken);

        using IApplication application = Application.Create();
        application.Init();
        using HarnessWindow window = new(
            application,
            dashboardService,
            workspaceService,
            frameworkService,
            snapshot,
            activeWorkspace,
            cancellationToken);
        await application.RunAsync(window, cancellationToken);
    }
}
