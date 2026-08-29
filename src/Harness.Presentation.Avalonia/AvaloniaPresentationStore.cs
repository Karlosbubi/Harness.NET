using System.Reactive.Subjects;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Events;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore(
    IDashboardService dashboardService,
    IAppearanceService appearanceService,
    IWorkspaceService workspaceService,
    IGoalService goalService,
    IGoalModelService goalModelService,
    IAgentDefaultsService agentDefaultsService,
    IRemoteCostService remoteCostService,
    IGoalWorkflowService goalWorkflowService,
    ISemanticIndexService semanticIndexService,
    IGoalAcceptanceService goalAcceptanceService,
    IApplicationOperationsService applicationOperationsService,
    ICapabilityApprovalService capabilityApprovalService,
    IFrameworkService frameworkService,
    ILogger<AvaloniaPresentationStore> logger,
    IModelProviderSettingsService? modelProviderSettingsService = null,
    IRemoteSpendPreferenceService? remoteSpendPreferenceService = null,
    IMcpSettingsService? mcpSettingsService = null,
    IVisualCaptureService? visualCaptureService = null,
    IResearchSettingsService? researchSettingsService = null,
    IDocumentationResearchService? documentationResearchService = null,
    IDependencyResearchService? dependencyResearchService = null,
    IInboundMcpSettingsService? inboundMcpSettingsService = null,
    IAgentToolExposureSettingsService? agentToolExposureSettingsService = null,
    IEditorIntelligenceSettingsService? editorIntelligenceSettingsService = null,
    IKeybindingSettingsService? keybindingSettingsService = null) : IDisposable
{
    private readonly BehaviorSubject<AvaloniaShellState> states = new(AvaloniaShellState.Initial);
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private readonly Dictionary<string, GoalId> selectedGoalsByWorkspace = new(StringComparer.Ordinal);
    private CancellationTokenSource? submission;
    private CancellationTokenSource? workflowExecution;
    private CancellationTokenSource? semanticExecution;

    internal IObservable<AvaloniaShellState> States => states;
    internal AvaloniaShellState Current => states.Value;
    internal event Action<WorkbenchEvent>? WorkbenchEventPublished;

    private void PublishWorkbenchEvent(
        WorkbenchEventSeverity severity,
        WorkbenchEventSource source,
        string message,
        WorkbenchEventNavigationTarget? navigationTarget = null) =>
        WorkbenchEventPublished?.Invoke(new(
            new(Guid.NewGuid().ToString("N")),
            severity,
            source,
            new(message),
            TimeProvider.System.GetUtcNow(),
            navigationTarget));

    internal async ValueTask LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            DashboardSnapshot dashboard = await dashboardService.RefreshProviderAsync(cancellationToken);
            AppearanceSnapshot appearance = await appearanceService.GetAsync(cancellationToken);
            AgentDefaultsSnapshot agentDefaults = await agentDefaultsService
                .DiscoverAvailableAsync(cancellationToken);
            ModelProviderSettingsSnapshot? providerSettings = modelProviderSettingsService is null
                ? null
                : await modelProviderSettingsService.GetAsync(cancellationToken);
            McpSettingsSnapshot? mcpSettings = mcpSettingsService is null
                ? null
                : await mcpSettingsService.GetAsync(cancellationToken);
            InboundMcpSettingsView? inboundMcpSettings = inboundMcpSettingsService is null
                ? null
                : await inboundMcpSettingsService.GetAsync(cancellationToken);
            AgentToolExposureSettings? agentToolExposure = agentToolExposureSettingsService is null
                ? null : await agentToolExposureSettingsService.GetAsync(cancellationToken);
            ResearchSettingsSnapshot? researchSettings = researchSettingsService is null
                ? null
                : await researchSettingsService.GetAsync(cancellationToken);
            RemoteSpendPreference remoteSpendPreference = remoteSpendPreferenceService is null
                ? RemoteSpendPreference.Default
                : await remoteSpendPreferenceService.GetAsync(cancellationToken);
            VisualCaptureSettingsSnapshot? visualCaptureSettings = visualCaptureService is null
                ? null
                : await visualCaptureService.GetSettingsAsync(cancellationToken);
            EditorIntelligenceSettingsSnapshot? editorIntelligenceSettings =
                editorIntelligenceSettingsService is null
                    ? null
                    : await editorIntelligenceSettingsService.GetAsync(cancellationToken);
            KeybindingSettingsSnapshot? keybindingSettings = keybindingSettingsService is null
                ? null
                : await keybindingSettingsService.GetAsync(cancellationToken);
            IReadOnlyList<WorkspaceView> workspaces = await workspaceService.ListAsync(cancellationToken);
            IReadOnlyList<GoalView> goals = await LoadGoalsAsync(workspaces, cancellationToken);
            Publish(Current with
            {
                Dashboard = dashboard,
                Appearance = appearance,
                Settings = Current.Settings with
                {
                    AgentDefaults = agentDefaults,
                    ProviderSettings = providerSettings,
                    McpSettings = mcpSettings,
                    InboundMcpSettings = inboundMcpSettings,
                    AgentToolExposure = agentToolExposure,
                    ResearchSettings = researchSettings,
                    VisualCaptureSettings = visualCaptureSettings,
                    EditorIntelligenceSettings = editorIntelligenceSettings,
                    KeybindingSettings = keybindingSettings,
                    RemoteSpendPreference = remoteSpendPreference,
                },
                Workspaces = Current.Workspaces with { Registered = workspaces },
                Goals = Current.Goals with { Items = goals },
                IsLoading = false,
                Error = null,
            });
            logger.LogInformation("Avalonia presentation state initialized");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Avalonia presentation initialization failed");
            Publish(Current with { IsLoading = false, Error = exception.Message });
        }
    }

    private async ValueTask UpdateDashboardAsync(
        Func<ValueTask<DashboardSnapshot>> action,
        string operation)
    {
        try
        {
            Publish(Current with { Error = null });
            Publish(Current with { Dashboard = await action() });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "{Operation} failed", operation);
            Publish(Current with { Error = exception.Message });
        }
    }

    private async ValueTask RunWorkspaceCommandAsync(
        Func<ValueTask> command,
        string operation)
    {
        if (Current.Workspaces.IsBusy)
        {
            return;
        }

        Publish(Current with
        {
            Workspaces = Current.Workspaces with { IsBusy = true, Status = null },
        });
        try
        {
            await command();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("{Operation} cancelled", operation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishWorkspaceFailure(exception, operation);
        }
        finally
        {
            Publish(Current with
            {
                Workspaces = Current.Workspaces with { IsBusy = false },
            });
        }
    }

    private async ValueTask ReloadWorkspaceContextAsync(
        string status,
        CancellationToken cancellationToken)
    {
        WorkspaceView? previous = ActiveWorkspace(Current.Workspaces.Registered);
        if (previous is not null && Current.Goals.SelectedGoalId is { } previousGoal)
        {
            selectedGoalsByWorkspace[previous.Id] = previousGoal;
        }

        IReadOnlyList<WorkspaceView> workspaces = await workspaceService.ListAsync(cancellationToken);
        DashboardSnapshot dashboard = await dashboardService.GetSnapshotAsync(cancellationToken);
        IReadOnlyList<GoalView> goals = await LoadGoalsAsync(workspaces, cancellationToken);
        WorkspaceView? active = ActiveWorkspace(workspaces);
        GoalId? selectedGoal = active is not null &&
            selectedGoalsByWorkspace.TryGetValue(active.Id, out GoalId? remembered) &&
            goals.Any(goal => goal.Id == remembered)
                ? remembered
                : null;
        GoalDetails details = selectedGoal is null
            ? GoalDetails.Empty
            : await LoadGoalDetailsAsync(selectedGoal, cancellationToken);
        Publish(Current with
        {
            Dashboard = dashboard,
            Workspaces = Current.Workspaces with
            {
                Registered = workspaces,
                EntryPoints = [],
                Status = status,
            },
            Goals = GoalManagementState.Initial with
            {
                Items = goals,
                SelectedGoalId = selectedGoal,
                CurrentPlan = details.Plan,
                ModelSelections = details.Selections,
                Cost = details.Cost,
                Workflow = details.Workflow,
                CommitApproval = details.CommitApproval,
                CapabilityApprovals = details.CapabilityApprovals,
            },
            Framework = FrameworkManagementState.Initial,
        });
    }

    private async ValueTask RunFrameworkCommandAsync(
        Func<WorkspaceView, ValueTask> command,
        string operation)
    {
        if (Current.Framework.IsBusy)
        {
            return;
        }

        WorkspaceView? workspace = ActiveWorkspace(Current.Workspaces.Registered);
        if (workspace is null)
        {
            Publish(Current with
            {
                Framework = Current.Framework with
                {
                    Status = "Select a workspace before managing its framework.",
                },
            });
            return;
        }

        Publish(Current with
        {
            Framework = Current.Framework with { IsBusy = true, Status = null },
        });
        try
        {
            await command(workspace);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("{Operation} cancelled", operation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "{Operation} failed", operation);
            Publish(Current with
            {
                Framework = Current.Framework with { Status = exception.Message },
            });
        }
        finally
        {
            Publish(Current with
            {
                Framework = Current.Framework with { IsBusy = false },
            });
        }
    }

    private async ValueTask RunGoalCommandAsync(
        Func<ValueTask> command,
        string operation)
    {
        if (Current.Goals.IsBusy)
        {
            return;
        }

        Publish(Current with
        {
            Goals = Current.Goals with { IsBusy = true, Status = null },
        });
        try
        {
            await command();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("{Operation} cancelled", operation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "{Operation} failed", operation);
            PublishGoalStatus(exception.Message);
        }
        finally
        {
            Publish(Current with
            {
                Goals = Current.Goals with { IsBusy = false },
            });
        }
    }

    private async ValueTask ReloadGoalsAsync(
        GoalId selectedGoalId,
        string status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GoalView> goals = await LoadGoalsAsync(
            Current.Workspaces.Registered,
            cancellationToken);
        GoalDetails details = await LoadGoalDetailsAsync(selectedGoalId, cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with
            {
                Items = goals,
                SelectedGoalId = selectedGoalId,
                CurrentPlan = details.Plan,
                ModelSelections = details.Selections,
                Cost = details.Cost,
                Workflow = details.Workflow,
                CommitApproval = details.CommitApproval,
                CapabilityApprovals = details.CapabilityApprovals,
                Status = status,
            },
        });
    }

    private static WorkspaceView? ActiveWorkspace(IReadOnlyList<WorkspaceView> workspaces) =>
        workspaces.FirstOrDefault(workspace => workspace.IsActive);

    private static string GoalTitle(string objective)
    {
        const int maximumCharacters = 72;
        string firstLine = objective
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? objective;
        string title = firstLine.Trim();
        if (title.Length <= maximumCharacters)
        {
            return title;
        }

        int lastSpace = title.LastIndexOf(' ', maximumCharacters - 1, maximumCharacters);
        int length = lastSpace > maximumCharacters / 2 ? lastSpace : maximumCharacters;
        return $"{title[..length].TrimEnd()}…";
    }

    private async ValueTask<IReadOnlyList<GoalView>> LoadGoalsAsync(
        IReadOnlyList<WorkspaceView> workspaces,
        CancellationToken cancellationToken)
    {
        WorkspaceView? active = ActiveWorkspace(workspaces);
        return active is null
            ? []
            : await goalService.ListAsync(active.Id, cancellationToken);
    }

    private void PublishGoalStatus(string status) =>
        Publish(Current with { Goals = Current.Goals with { Status = status } });

    private async ValueTask<GoalDetails> LoadGoalDetailsAsync(
        GoalId goalId,
        CancellationToken cancellationToken)
    {
        PlanView? plan = await goalService.GetCurrentPlanAsync(goalId, cancellationToken);
        IReadOnlyList<GoalModelSelectionView> selections =
            await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
        RemoteCostReport? cost = await remoteCostService.GetAsync(goalId, cancellationToken);
        GoalWorkflowSnapshot? workflow = await goalWorkflowService.GetLatestAsync(
            goalId,
            cancellationToken);
        GoalCommitApprovalView? commitApproval = workflow is null
            ? null
            : await goalAcceptanceService.GetAsync(goalId, workflow.Id, cancellationToken);
        CapabilityApprovalSnapshot capabilityApprovals = await capabilityApprovalService.ListAsync(
            goalId.Value,
            cancellationToken);
        return new(plan, selections, cost, workflow, commitApproval, capabilityApprovals.Items);
    }

    private async ValueTask RunSemanticOperationAsync(
        GoalId goalId,
        Func<SemanticIndexRequest, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken,
        string operationName)
    {
        if (Current.Goals.IsBusy || Current.Goals.IsSemanticRunning)
        {
            return;
        }

        SemanticIndexRequest? request = SemanticRequest(goalId);
        if (request is null)
        {
            PublishGoalStatus("An active workspace is required for semantic context.");
            return;
        }

        semanticExecution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with
            {
                IsBusy = true,
                IsSemanticRunning = true,
                Status = $"{operationName} started.",
            },
        });
        try
        {
            await operation(request, semanticExecution.Token);
            RemoteCostReport? cost = await remoteCostService.GetAsync(
                goalId,
                semanticExecution.Token);
            Publish(Current with { Goals = Current.Goals with { Cost = cost } });
        }
        catch (OperationCanceledException) when (semanticExecution.IsCancellationRequested)
        {
            logger.LogInformation("{Operation} cancelled", operationName);
            PublishGoalStatus($"{operationName} cancelled.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Operation} failed", operationName);
            PublishGoalStatus(exception.Message);
        }
        finally
        {
            semanticExecution.Dispose();
            semanticExecution = null;
            Publish(Current with
            {
                Goals = Current.Goals with { IsBusy = false, IsSemanticRunning = false },
            });
        }
    }

    private SemanticIndexRequest? SemanticRequest(GoalId goalId)
    {
        WorkspaceView? workspace = ActiveWorkspace(Current.Workspaces.Registered);
        return workspace is null
            ? null
            : new(
                workspace.Id,
                goalId.Value,
                SemanticPrivacyPolicy.NoCollectionAndZeroDataRetention);
    }

    private async ValueTask ReloadCapabilityApprovalsAsync(
        GoalId goalId,
        string status,
        CancellationToken cancellationToken)
    {
        CapabilityApprovalSnapshot snapshot = await capabilityApprovalService.ListAsync(
            goalId.Value,
            cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with
            {
                CapabilityApprovals = snapshot.Items,
                Status = snapshot.Error ?? status,
            },
        });
    }

    private static string CatalogStatus(GoalModelCatalog catalog) =>
        catalog.Issues.Count == 0
            ? $"Discovered {catalog.Models.Count} chat model(s); no inference was performed."
            : $"Discovered {catalog.Models.Count} chat model(s). " +
              string.Join(" | ", catalog.Issues.Select(issue =>
                  $"{issue.Provider.Value}: {issue.Message}"));

    private static string WorkflowStatus(GoalWorkflowSnapshot snapshot) =>
        snapshot.Activities.Count == 0
            ? $"Workflow {snapshot.State}."
            : $"Workflow {snapshot.State}: {snapshot.Activities[^1].Summary.Value}";

    private sealed record GoalDetails(
        PlanView? Plan,
        IReadOnlyList<GoalModelSelectionView> Selections,
        RemoteCostReport? Cost,
        GoalWorkflowSnapshot? Workflow,
        GoalCommitApprovalView? CommitApproval,
        IReadOnlyList<CapabilityApprovalView> CapabilityApprovals)
    {
        internal static GoalDetails Empty { get; } = new(null, [], null, null, null, []);
    }

    private void PublishWorkspaceFailure(Exception exception, string operation)
    {
        logger.LogError(exception, "{Operation} failed", operation);
        Publish(Current with
        {
            Workspaces = Current.Workspaces with { Status = exception.Message },
        });
    }

    private void Publish(AvaloniaShellState state) => states.OnNext(state);
}
