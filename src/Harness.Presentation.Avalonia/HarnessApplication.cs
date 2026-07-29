using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Dock.Avalonia.Themes.Fluent;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed class HarnessApplication(
    AvaloniaPresentationStore store,
    HarnessThemeController themeController,
    IWorkspaceInspectionService inspectionService,
    IWorkbenchLayoutService layoutService,
    CancellationToken cancellationToken) : Application
{
    internal MainWindow? MainWindow { get; private set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Harness.Presentation.Avalonia/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new(
                store,
                themeController,
                inspectionService,
                layoutService,
                cancellationToken);
            desktop.MainWindow = MainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
