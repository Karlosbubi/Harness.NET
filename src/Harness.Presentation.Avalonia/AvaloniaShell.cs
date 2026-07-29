using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed class AvaloniaShell(
    AvaloniaPresentationStore store,
    HarnessThemeController themeController,
    IWorkspaceInspectionService inspectionService,
    IWorkbenchLayoutService layoutService) : IAvaloniaShell
{
    public ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        HarnessApplication? application = null;
        AppBuilder.Configure(() => application = new(
                store,
                themeController,
                inspectionService,
                layoutService,
                cancellationToken))
            .UsePlatformDetect()
            .AfterSetup(_ => cancellationToken.Register(() =>
                Dispatcher.UIThread.Post(() => application?.MainWindow?.Close())))
            .StartWithClassicDesktopLifetime(
                [],
                ShutdownMode.OnMainWindowClose);
        return ValueTask.CompletedTask;
    }
}
