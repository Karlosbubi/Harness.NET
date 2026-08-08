using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Retrieval;
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
    IGoalModelService goalModelService,
    IAgentDefaultsService agentDefaultsService,
    IGoalWorkflowService goalWorkflowService,
    IGoalAcceptanceService goalAcceptanceService,
    ISemanticIndexService semanticIndexService,
    IApplicationOperationsService operationsService) : ITerminalShell
{
    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        DashboardSnapshot snapshot = await dashboardService.RefreshProviderAsync(cancellationToken);
        AgentDefaultsSnapshot agentDefaults = await agentDefaultsService
            .DiscoverAvailableAsync(cancellationToken);
        WorkspaceView? activeWorkspace = await workspaceService.GetActiveAsync(cancellationToken);
        IReadOnlyList<GoalView> goals = activeWorkspace is null
            ? []
            : await goalService.ListAsync(activeWorkspace.Id, cancellationToken);
        using IApplication application = Application.Create();
        application.Init();
        using HarnessWindow window = new(
            application,
            dashboardService,
            workspaceService,
            frameworkService,
            goalService,
            remoteCostService,
            goalModelService,
            goalWorkflowService,
            goalAcceptanceService,
            semanticIndexService,
            operationsService,
            agentDefaults,
            snapshot,
            activeWorkspace,
            goals,
            cancellationToken);
        await application.RunAsync(window, cancellationToken);
    }
}
