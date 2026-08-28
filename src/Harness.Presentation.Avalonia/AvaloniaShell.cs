using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;
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

internal sealed class AvaloniaShell(
    AvaloniaPresentationStore store,
    HarnessThemeController themeController,
    IRunOutputService runOutputService,
    IToolEvidenceService toolEvidenceService,
    IAgentActivityReader agentActivityReader,
    IWorkbenchInspectionService inspectionService,
    IDeveloperGitService developerGitService,
    IWorkbenchDocumentService documentService,
    IWorkbenchCodeIntelligenceService codeIntelligenceService,
    IWorkspaceMutationService mutationService,
    IWorkbenchLayoutService layoutService,
    IProjectUserSecretsService projectUserSecretsService,
    IDeveloperProjectExecutionService developerExecutionService,
    AvaloniaInboundMcpUiBridge inboundMcpUiBridge) : IAvaloniaShell
{
    public ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        HarnessApplication? application = null;
        AppBuilder.Configure(() => application = new(
                store,
                themeController,
                runOutputService,
                toolEvidenceService,
                agentActivityReader,
                inspectionService,
                developerGitService,
                documentService,
                codeIntelligenceService,
                mutationService,
                layoutService,
                projectUserSecretsService,
                developerExecutionService,
                inboundMcpUiBridge,
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
