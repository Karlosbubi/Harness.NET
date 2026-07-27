using Harness.BusinessLogic.Dashboard;
using Terminal.Gui.App;

namespace Harness.Presentation.Terminal;

internal sealed class TerminalGuiShell(IDashboardService dashboardService) : ITerminalShell
{
    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        DashboardSnapshot snapshot = await dashboardService.GetSnapshotAsync(cancellationToken);

        using IApplication application = Application.Create();
        application.Init();
        using HarnessWindow window = new(
            application,
            dashboardService,
            snapshot,
            cancellationToken);
        await application.RunAsync(window, cancellationToken);
    }
}
