using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Dock.Avalonia.Themes.Fluent;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed class HarnessApplication(
    AvaloniaPresentationStore store,
    HarnessThemeController themeController,
    IRunOutputService runOutputService,
    IWorkbenchInspectionService inspectionService,
    IWorkbenchDocumentService documentService,
    IWorkbenchCodeIntelligenceService codeIntelligenceService,
    IWorkspaceMutationService mutationService,
    IWorkbenchLayoutService layoutService,
    IProjectUserSecretsService projectUserSecretsService,
    IDeveloperProjectExecutionService developerExecutionService,
    AvaloniaInboundMcpUiBridge inboundMcpUiBridge,
    CancellationToken cancellationToken) : Application
{
    internal MainWindow? MainWindow { get; private set; }

    public override void Initialize()
    {
        AccessibilityTreeSemantics.Register();
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Harness.Presentation.Avalonia/"))
        {
            Source = new Uri("avares://Harness.Presentation.Avalonia/WorkbenchStyles.axaml"),
        });
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
                runOutputService,
                inspectionService,
                documentService,
                codeIntelligenceService,
                mutationService,
                layoutService,
                projectUserSecretsService,
                developerExecutionService,
                cancellationToken);
            inboundMcpUiBridge.Attach(MainWindow);
            MainWindow.Closed += (_, _) => inboundMcpUiBridge.Detach(MainWindow);
            desktop.MainWindow = MainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
