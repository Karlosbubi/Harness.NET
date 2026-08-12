using System.Reactive.Subjects;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Editor;
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

internal sealed class AvaloniaPresentationStore(
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
    IEditorIntelligenceSettingsService? editorIntelligenceSettingsService = null) : IDisposable
{
    private readonly BehaviorSubject<AvaloniaShellState> states = new(AvaloniaShellState.Initial);
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private readonly Dictionary<string, GoalId> selectedGoalsByWorkspace = new(StringComparer.Ordinal);
    private CancellationTokenSource? submission;
    private CancellationTokenSource? workflowExecution;
    private CancellationTokenSource? semanticExecution;

    internal IObservable<AvaloniaShellState> States => states;
    internal AvaloniaShellState Current => states.Value;

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

    internal async ValueTask SaveEditorIntelligenceSettingsAsync(
        EditorIntelligencePreferences preferences,
        CancellationToken cancellationToken)
    {
        if (editorIntelligenceSettingsService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                IsBusy = true,
                Status = "Saving editor intelligence settings…",
            },
        });
        try
        {
            EditorIntelligenceSettingsSnapshot saved =
                await editorIntelligenceSettingsService.SaveAsync(preferences, cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    EditorIntelligenceSettings = saved,
                    IsBusy = false,
                    Status = "Editor intelligence settings saved.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    IsBusy = false,
                    Status = $"Editor settings were not saved: {exception.Message}",
                },
            });
        }
    }

    internal async ValueTask SaveAgentToolExposureAsync(
        IReadOnlyList<AgentToolModuleId> modules, CancellationToken cancellationToken)
    {
        if (agentToolExposureSettingsService is null) return;
        AgentToolExposureSettings saved = await agentToolExposureSettingsService.SaveAsync(
            new(modules), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            { AgentToolExposure = saved, Status = "Agent tool exposure defaults saved." }
        });
    }

    internal async ValueTask SaveInboundMcpAsync(
        InboundControlSettings settings,
        CancellationToken cancellationToken)
    {
        if (inboundMcpSettingsService is null) return;
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Applying inbound MCP settings…" }
        });
        try
        {
            InboundMcpSettingsView snapshot = await inboundMcpSettingsService.SaveAsync(
                settings, cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    InboundMcpSettings = snapshot,
                    IsBusy = false,
                    Status = snapshot.Status.IsRunning ? "Inbound MCP server is active." :
                    snapshot.Status.Error ?? "Inbound MCP server is disabled."
                }
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                { IsBusy = false, Status = exception.Message }
            });
        }
    }

    internal async ValueTask<string?> RotateInboundMcpTokenAsync(CancellationToken cancellationToken)
    {
        if (inboundMcpSettingsService is null) return null;
        InboundControlTokenRotation rotation = await inboundMcpSettingsService.RotateTokenAsync(cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                InboundMcpSettings = rotation.Settings,
                Status = "Bearer token rotated and copied once; existing clients were revoked."
            }
        });
        return rotation.OneTimeBearerToken;
    }

    internal async ValueTask DisconnectInboundMcpClientAsync(
        InboundControlClientId clientId,
        CancellationToken cancellationToken)
    {
        if (inboundMcpSettingsService is null) return;
        InboundMcpSettingsView snapshot = await inboundMcpSettingsService.DisconnectAsync(
            clientId, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            { InboundMcpSettings = snapshot, Status = $"Disconnected {clientId.Value}." }
        });
    }

    internal async ValueTask ResetInboundMcpEvaluationAsync(CancellationToken cancellationToken)
    {
        if (inboundMcpSettingsService is null) return;
        try
        {
            InboundControlEvaluationReset reset = await inboundMcpSettingsService
                .ResetEvaluationAsync(cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    InboundMcpSettings = reset.Settings,
                    Status = $"Evaluation fixture reset to {reset.Head[..Math.Min(12, reset.Head.Length)]}; {reset.ChangedFiles} changes remain."
                }
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(Current with { Settings = Current.Settings with { Status = exception.Message } });
        }
    }

    internal async ValueTask SaveResearchSettingsAsync(
        ResearchSettingsUpdate update,
        CancellationToken cancellationToken)
    {
        if (researchSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                { Status = "Documentation and dependency settings are unavailable." }
            });
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Saving documentation and dependency settings…" }
        });
        ResearchSettingsResult result = await researchSettingsService.SaveAsync(update, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                ResearchSettings = result.Snapshot ?? Current.Settings.ResearchSettings,
                IsBusy = false,
                Status = result.Error ?? "Documentation and dependency settings saved.",
            }
        });
    }

    internal async ValueTask CleanupResearchCacheAsync(CancellationToken cancellationToken)
    {
        if (researchSettingsService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Cleaning documentation cache…" }
        });
        ResearchSettingsSnapshot snapshot = await researchSettingsService.CleanupCacheAsync(cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                ResearchSettings = snapshot,
                IsBusy = false,
                Status = "Documentation cache retention applied.",
            }
        });
    }

    internal async ValueTask LookupDocumentationAsync(
        string library,
        string? version,
        string question,
        CancellationToken cancellationToken)
    {
        if (documentationResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Looking up documentation on demand…" }
        });
        DocumentationLookupResult result = await documentationResearchService.LookupAsync(new(
            GoalId: null,
            new(library),
            string.IsNullOrWhiteSpace(version) ? null : new(version),
            new(question)), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                DocumentationLookup = result,
                IsBusy = false,
                Status = result.Error ?? $"Documentation lookup returned {result.Results.Count} result(s).",
            }
        });
    }

    internal async ValueTask InspectDependenciesAsync(CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Reading dependency evidence…" }
        });
        DependencyInspectionResult result = await dependencyResearchService.InspectAsync(
            new(GoalId: null), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                DependencyInspection = result,
                IsBusy = false,
                Status = result.Error ?? $"Inspected {result.Projects.Count} project(s) without restoring.",
            }
        });
    }

    internal async ValueTask ValidatePackageCandidateAsync(
        string package,
        string version,
        bool allowPrerelease,
        CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Validating exact package evidence…" }
        });
        PackageCandidateValidationResult result = await dependencyResearchService
            .ValidateCandidateAsync(new(null, new(package), new(version), allowPrerelease),
                cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                PackageCandidateValidation = result,
                IsBusy = false,
                Status = result.Error ?? $"Candidate decision: {result.Decision}.",
            }
        });
    }

    internal async ValueTask PreviewSbomAsync(CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Generating deterministic SBOM preview…" }
        });
        SbomPreviewResult result = await dependencyResearchService.PreviewSbomAsync(
            new(GoalId: null), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                SbomPreview = result,
                IsBusy = false,
                Status = result.Error ?? $"Generated {result.Sbom!.Format} preview.",
            }
        });
    }

    internal async ValueTask PreviewPackageChangeAsync(
        string package,
        string version,
        bool allowPrerelease,
        CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Preparing package and SBOM diff…" }
        });
        PackageChangePreviewResult result = await dependencyResearchService.PreviewPackageChangeAsync(
            new(null, new(package), new(version), allowPrerelease), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                PackageChangePreview = result,
                IsBusy = false,
                Status = result.Error ?? "Package and SBOM diff ready; no project files were changed.",
            }
        });
    }

    internal async ValueTask ExportSbomAsync(
        string path,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Exporting explicitly requested SBOM…" }
        });
        SbomExportResult result = await dependencyResearchService.ExportSbomAsync(
            new(null, new(path), overwrite), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                SbomExport = result,
                IsBusy = false,
                Status = result.Error ?? $"SBOM exported to {result.Path.Value}.",
            }
        });
    }

    internal async ValueTask SaveVisualCaptureSettingsAsync(
        VisualCapturePreferences preferences,
        CancellationToken cancellationToken)
    {
        if (visualCaptureService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                { Status = "Visual capture is unavailable." }
            });
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Saving visual verification settings…" }
        });
        VisualCaptureSettingsResult result = await visualCaptureService.SaveSettingsAsync(
            preferences, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                VisualCaptureSettings = result.Snapshot ?? Current.Settings.VisualCaptureSettings,
                IsBusy = false,
                Status = result.Error ?? "Visual verification settings saved.",
            }
        });
    }

    internal async ValueTask CaptureVisualAsync(
        VisualCaptureTarget target,
        VisualCaptureUiScale? uiScale,
        VisualCaptureParentWindow? parentWindow,
        CancellationToken cancellationToken)
    {
        GoalId? goalId = Current.Goals.SelectedGoalId;
        if (visualCaptureService is null || goalId is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                { Status = "Select a goal before capturing visual evidence." }
            });
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Waiting for portal consent…" }
        });
        VisualCaptureResult result = await visualCaptureService.CaptureAsync(new(
            goalId,
            new ToolCorrelationId($"developer-capture-{Guid.NewGuid():N}"),
            VisualCaptureInitiator.Developer,
            new("Manual visual verification"),
            new("Harness.NET"),
            target,
            TimeProvider.System.GetUtcNow(),
            parentWindow,
            uiScale), cancellationToken);
        await RefreshVisualCapturesAsync(cancellationToken);
        if (result.Capture is not null)
        {
            await InspectVisualCaptureAsync(result.Capture.Id, cancellationToken);
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = false, Status = result.Error ?? "Visual evidence captured." }
        });
    }

    internal async ValueTask RefreshVisualCapturesAsync(CancellationToken cancellationToken)
    {
        GoalId? goalId = Current.Goals.SelectedGoalId;
        if (visualCaptureService is null || goalId is null)
        {
            return;
        }
        VisualCaptureListResult result = await visualCaptureService.ListAsync(goalId, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            { VisualCaptures = result.Captures, Status = result.Error ?? Current.Settings.Status }
        });
    }

    internal async ValueTask InspectVisualCaptureAsync(
        VisualCaptureId captureId,
        CancellationToken cancellationToken)
    {
        GoalId? goalId = Current.Goals.SelectedGoalId;
        if (visualCaptureService is null || goalId is null)
        {
            return;
        }
        VisualCaptureInspectionResult result = await visualCaptureService.InspectAsync(
            goalId, captureId, VisualCaptureModelAccess.Local, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                SelectedVisualCapture = result.Content,
                Status = result.Error ?? "Showing the exact stored frame available to agents.",
            }
        });
    }

    internal async ValueTask DeleteVisualCaptureAsync(
        VisualCaptureId captureId,
        CancellationToken cancellationToken)
    {
        GoalId? goalId = Current.Goals.SelectedGoalId;
        if (visualCaptureService is null || goalId is null)
        {
            return;
        }
        await visualCaptureService.DeleteAsync(goalId, captureId, cancellationToken);
        Publish(Current with { Settings = Current.Settings with { SelectedVisualCapture = null } });
        await RefreshVisualCapturesAsync(cancellationToken);
    }

    internal async ValueTask RefreshMcpAsync(CancellationToken cancellationToken)
    {
        if (mcpSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "MCP configuration is unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Discovering MCP tools…" },
        });
        try
        {
            McpSettingsSnapshot snapshot = await mcpSettingsService.RefreshAsync(cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    McpSettings = snapshot,
                    IsBusy = false,
                    Status = "MCP discovery refreshed without inference.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MCP discovery failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal async ValueTask SaveMcpConnectionAsync(
        McpConnectionSettingsUpdate request,
        CancellationToken cancellationToken)
    {
        if (mcpSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "MCP configuration is unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Saving MCP connection…" },
        });
        try
        {
            McpSettingsResult result = await mcpSettingsService.SaveAsync(request, cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    McpSettings = result.Snapshot ?? Current.Settings.McpSettings,
                    IsBusy = false,
                    Status = result.Error ?? "MCP connection saved. Restart Harness.NET to apply it.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MCP connection save failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal async ValueTask DeleteMcpConnectionAsync(
        McpConnectionName name,
        CancellationToken cancellationToken)
    {
        if (mcpSettingsService is null)
        {
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Removing MCP connection…" },
        });
        try
        {
            McpSettingsResult result = await mcpSettingsService.DeleteAsync(name, cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    McpSettings = result.Snapshot ?? Current.Settings.McpSettings,
                    IsBusy = false,
                    Status = result.Error ?? "MCP connection removed. Restart Harness.NET to apply it.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MCP connection removal failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal void SetComposerText(string value) =>
        Publish(Current with { ComposerText = value });

    internal async ValueTask SubmitComposerAsync(CancellationToken cancellationToken)
    {
        if (Current.IsStreaming || string.IsNullOrWhiteSpace(Current.ComposerText))
        {
            return;
        }

        if (Current.Goals.SelectedGoal is not null)
        {
            await SubmitAsync(cancellationToken);
            return;
        }

        WorkspaceView? workspace = ActiveWorkspace(Current.Workspaces.Registered);
        if (workspace is null)
        {
            Publish(Current with { Error = "Open a workspace before creating a goal." });
            return;
        }

        if (!workspace.IsTrusted)
        {
            Publish(Current with { Error = "Trust the active workspace before creating a goal." });
            return;
        }

        string objective = Current.ComposerText.Trim();
        await CreateGoalAsync(
            new(
                workspace.Id,
                GoalTitle(objective),
                objective,
                new ReviewCycleLimit(3),
                Current.Settings.RemoteSpendPreference.ToGoalBudget()),
            cancellationToken);
        if (Current.Goals.SelectedGoal is not null)
        {
            Publish(Current with { ComposerText = string.Empty, Error = null });
        }
    }

    internal async ValueTask SubmitAsync(CancellationToken cancellationToken)
    {
        if (Current.IsStreaming || string.IsNullOrWhiteSpace(Current.ComposerText))
        {
            return;
        }

        await commandGate.WaitAsync(cancellationToken);
        try
        {
            if (Current.IsStreaming)
            {
                return;
            }

            string instruction = Current.ComposerText.Trim();
            submission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Publish(Current with
            {
                ComposerText = string.Empty,
                IsStreaming = true,
                Error = null,
            });
            logger.LogInformation("Avalonia conversation submission started");
            await foreach (DashboardSnapshot dashboard in dashboardService
                               .SubmitAsync(instruction, submission.Token)
                               .WithCancellation(submission.Token))
            {
                Publish(Current with { Dashboard = dashboard });
            }
        }
        catch (OperationCanceledException) when (submission?.IsCancellationRequested is true)
        {
            logger.LogInformation("Avalonia conversation submission cancelled");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Avalonia conversation submission failed");
            Publish(Current with { Error = exception.Message });
        }
        finally
        {
            submission?.Dispose();
            submission = null;
            Publish(Current with { IsStreaming = false });
            commandGate.Release();
        }
    }

    internal void CancelSubmission() => submission?.Cancel();

    internal async ValueTask RefreshProviderAsync(CancellationToken cancellationToken) =>
        await UpdateDashboardAsync(
            () => dashboardService.RefreshProviderAsync(cancellationToken),
            "Provider refresh");

    internal async ValueTask SelectModelAsync(string model, CancellationToken cancellationToken) =>
        await UpdateDashboardAsync(
            () => dashboardService.SelectModelAsync(model, cancellationToken),
            "Model selection");

    internal async ValueTask RefreshThemesAsync(CancellationToken cancellationToken)
    {
        AppearanceSnapshot appearance = await appearanceService.GetAsync(cancellationToken);
        Publish(Current with { Appearance = appearance, Error = null });
    }

    internal async ValueTask SelectThemeAsync(string themeId, CancellationToken cancellationToken)
    {
        AppearanceSelectionResult result = await appearanceService.SelectAsync(
            new(themeId), cancellationToken);
        Publish(Current with
        {
            Appearance = result.Snapshot,
            Error = result.Error,
        });
    }

    internal async ValueTask DiscoverAgentDefaultsAsync(CancellationToken cancellationToken)
    {
        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Discovering chat models…" },
            Error = null,
        });
        try
        {
            AgentDefaultsSnapshot snapshot = await agentDefaultsService
                .DiscoverAvailableAsync(cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    AgentDefaults = snapshot,
                    IsBusy = false,
                    Status = snapshot.Issues.Count == 0
                        ? $"Discovered {snapshot.Models.Count} chat model(s)."
                        : $"Discovered {snapshot.Models.Count} chat model(s) with " +
                          $"{snapshot.Issues.Count} provider issue(s).",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Agent default model discovery failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
                Error = exception.Message,
            });
        }
    }

    internal async ValueTask UpdateModelProviderAsync(
        ModelProviderSettingsUpdate request,
        CancellationToken cancellationToken)
    {
        if (modelProviderSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "Provider configuration is unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Saving provider configuration…" },
        });
        try
        {
            ModelProviderSettingsResult result = await modelProviderSettingsService.UpdateAsync(
                request,
                cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    ProviderSettings = result.Snapshot ?? Current.Settings.ProviderSettings,
                    IsBusy = false,
                    Status = result.Error ?? "Provider configuration saved. Restart Harness.NET to apply it.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Provider configuration update failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal async ValueTask SetModelProviderCredentialAsync(
        ModelProviderCredentialUpdate request,
        CancellationToken cancellationToken)
    {
        if (modelProviderSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "Provider credentials are unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Saving provider credential…" },
        });
        try
        {
            ModelProviderSettingsResult result = await modelProviderSettingsService.SetCredentialAsync(
                request,
                cancellationToken);
            ModelProviderSettingsView? provider = result.Snapshot?.Providers.FirstOrDefault(item =>
                item.Provider == request.Provider);
            bool canRefreshActiveProvider = provider is { RequiresRestart: false };
            AgentDefaultsSnapshot? defaults = canRefreshActiveProvider
                ? await agentDefaultsService.DiscoverAvailableAsync(cancellationToken)
                : Current.Settings.AgentDefaults;
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    ProviderSettings = result.Snapshot ?? Current.Settings.ProviderSettings,
                    AgentDefaults = defaults,
                    IsBusy = false,
                    Status = result.Error ?? (canRefreshActiveProvider
                        ? "Provider credential saved to Secret Service; catalog refreshed."
                        : "Provider credential saved to Secret Service. Restart Harness.NET to activate the pending provider configuration."),
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Provider credential update failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal async ValueTask UpdateAgentDefaultAsync(
        AgentRole role,
        GoalModelCandidate candidate,
        CancellationToken cancellationToken)
    {
        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = $"Saving {role} defaults…" },
            Error = null,
        });
        try
        {
            AgentRoleDefaultUpdateResult result = await agentDefaultsService.UpdateAsync(new(
                role,
                candidate.Provider,
                candidate.Model), cancellationToken);
            if (result.Value is null)
            {
                Publish(Current with
                {
                    Settings = Current.Settings with { IsBusy = false, Status = result.Error },
                    Error = result.Error,
                });
                return;
            }

            AgentDefaultsSnapshot current = Current.Settings.AgentDefaults ??
                new([], [], [], [], []);
            AgentDefaultsSnapshot updated = current with
            {
                Roles = current.Roles
                    .Where(item => item.Role != role)
                    .Append(result.Value)
                    .OrderBy(item => item.Role)
                    .ToArray(),
                DefaultIssues = current.DefaultIssues
                    .Where(issue => issue.Role != role)
                    .ToArray(),
            };
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    AgentDefaults = updated,
                    IsBusy = false,
                    Status = $"Saved {role} defaults.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Agent default update failed for {Role}", role);
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
                Error = exception.Message,
            });
        }
    }

    internal async ValueTask UpdateRemoteSpendPreferenceAsync(
        RemoteSpendPreference preference,
        CancellationToken cancellationToken)
    {
        if (remoteSpendPreferenceService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "Remote-spend preferences are unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with
            {
                IsBusy = true,
                Status = "Saving default remote-spend policy…",
            },
        });
        RemoteSpendPreferenceResult result = await remoteSpendPreferenceService.UpdateAsync(
            preference,
            cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                RemoteSpendPreference = result.Preference,
                IsBusy = false,
                Status = result.Error ?? "Default remote-spend policy saved.",
            },
            Error = result.Error,
        });
    }

    internal void SetRepositoryPath(string value) =>
        Publish(Current with
        {
            Workspaces = Current.Workspaces with
            {
                RepositoryPath = value,
                EntryPoints = [],
                Status = null,
            },
        });

    internal void SetWorkspaceStatus(string value) =>
        Publish(Current with
        {
            Workspaces = Current.Workspaces with { Status = value },
        });

    internal async ValueTask RefreshWorkspacesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WorkspaceView> workspaces = await workspaceService.ListAsync(cancellationToken);
            Publish(Current with
            {
                Workspaces = Current.Workspaces with
                {
                    Registered = workspaces,
                    Status = null,
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishWorkspaceFailure(exception, "Workspace refresh");
        }
    }

    internal async ValueTask InspectWorkspaceAsync(CancellationToken cancellationToken)
    {
        string path = Current.Workspaces.RepositoryPath.Trim();
        if (path.Length == 0)
        {
            Publish(Current with
            {
                Workspaces = Current.Workspaces with { Status = "Enter a repository path." },
            });
            return;
        }

        await RunWorkspaceCommandAsync(async () =>
        {
            WorkspaceResult result = await workspaceService.InspectAsync(path, cancellationToken);
            Publish(Current with
            {
                Workspaces = Current.Workspaces with
                {
                    EntryPoints = result.EntryPoints,
                    Status = result.Error ?? $"Found {result.EntryPoints.Count} tracked .NET entry point(s).",
                },
            });
        }, "Workspace inspection");
    }

    internal async ValueTask RegisterWorkspaceAsync(
        string entryPoint,
        CancellationToken cancellationToken)
    {
        string path = Current.Workspaces.RepositoryPath.Trim();
        await RunWorkspaceCommandAsync(async () =>
        {
            WorkspaceResult result = await workspaceService.RegisterAsync(
                path,
                entryPoint,
                cancellationToken);
            if (result.Workspace is null)
            {
                Publish(Current with
                {
                    Workspaces = Current.Workspaces with
                    {
                        EntryPoints = result.EntryPoints,
                        Status = result.Error ?? "Workspace registration failed.",
                    },
                });
                return;
            }

            await ReloadWorkspaceContextAsync(
                $"Registered and selected {result.Workspace.Name}. Trust it before running tools.",
                cancellationToken);
        }, "Workspace registration");
    }

    internal async ValueTask SelectWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await RunWorkspaceCommandAsync(async () =>
        {
            WorkspaceView selected = await workspaceService.SelectAsync(
                workspaceId,
                cancellationToken);
            await ReloadWorkspaceContextAsync($"Selected {selected.Name}.", cancellationToken);
        }, "Workspace selection");

    internal async ValueTask SetWorkspaceTrustAsync(
        string workspaceId,
        bool isTrusted,
        CancellationToken cancellationToken) =>
        await RunWorkspaceCommandAsync(async () =>
        {
            WorkspaceResult result = await workspaceService.SetTrustAsync(
                workspaceId,
                isTrusted,
                cancellationToken);
            string name = result.Workspace?.Name ?? "workspace";
            await ReloadWorkspaceContextAsync(
                isTrusted ? $"Trusted {name}." : $"Revoked trust for {name}.",
                cancellationToken);
        }, isTrusted ? "Workspace trust" : "Workspace trust revocation");

    internal async ValueTask RefreshFrameworkAsync(CancellationToken cancellationToken) =>
        await RunFrameworkCommandAsync(async workspace =>
        {
            FrameworkSnapshot snapshot = await frameworkService.GetEffectiveAsync(
                workspace.Id,
                workspace.RootPath,
                cancellationToken);
            Publish(Current with
            {
                Framework = Current.Framework with
                {
                    Snapshot = snapshot,
                    Status = snapshot.IsValid
                        ? "Effective framework loaded."
                        : "Framework issues require attention.",
                },
            });
        }, "Framework inspection");

    internal async ValueTask SetPrivateFrameworkOverlayAsync(
        string? content,
        CancellationToken cancellationToken) =>
        await RunFrameworkCommandAsync(async workspace =>
        {
            FrameworkSnapshot snapshot = await frameworkService.SetPrivateOverlayAsync(
                workspace.Id,
                workspace.RootPath,
                content,
                cancellationToken);
            Publish(Current with
            {
                Framework = Current.Framework with
                {
                    Snapshot = snapshot,
                    Status = string.IsNullOrWhiteSpace(content)
                        ? "Private workspace overlay removed."
                        : "Private workspace overlay updated.",
                },
            });
        }, "Private framework overlay update");

    internal async ValueTask RefreshGoalsAsync(CancellationToken cancellationToken)
    {
        await RunGoalCommandAsync(async () =>
        {
            IReadOnlyList<GoalView> goals = await LoadGoalsAsync(
                Current.Workspaces.Registered,
                cancellationToken);
            GoalId? selectedId = Current.Goals.SelectedGoalId;
            GoalView? selected = selectedId is null
                ? null
                : goals.FirstOrDefault(goal => goal.Id == selectedId);
            PlanView? plan = selected is null
                ? null
                : await goalService.GetCurrentPlanAsync(selected.Id, cancellationToken);
            GoalDetails details = selected is null
                ? GoalDetails.Empty
                : await LoadGoalDetailsAsync(selected.Id, cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    Items = goals,
                    SelectedGoalId = selected?.Id,
                    CurrentPlan = plan,
                    ModelSelections = details.Selections,
                    Cost = details.Cost,
                    Workflow = details.Workflow,
                    CommitApproval = details.CommitApproval,
                    CapabilityApprovals = details.CapabilityApprovals,
                    Status = goals.Count == 0 ? "Create the first goal." : $"{goals.Count} goal(s).",
                },
            });
        }, "Goal refresh");
    }

    internal async ValueTask SelectGoalAsync(
        GoalId goalId,
        CancellationToken cancellationToken)
    {
        if (!Current.Goals.Items.Any(goal => goal.Id == goalId))
        {
            return;
        }

        await RunGoalCommandAsync(async () =>
        {
            GoalDetails details = await LoadGoalDetailsAsync(goalId, cancellationToken);
            WorkspaceView? active = ActiveWorkspace(Current.Workspaces.Registered);
            if (active is not null)
            {
                selectedGoalsByWorkspace[active.Id] = goalId;
            }

            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    SelectedGoalId = goalId,
                    CurrentPlan = details.Plan,
                    ModelCatalog = null,
                    ModelSelections = details.Selections,
                    Cost = details.Cost,
                    Workflow = details.Workflow,
                    SemanticStatus = null,
                    SemanticRebuild = null,
                    SemanticSearch = null,
                    CommitPreview = null,
                    CommitApproval = details.CommitApproval,
                    CapabilityApprovals = details.CapabilityApprovals,
                    Status = null,
                },
            });
        }, "Goal selection");
    }

    internal async ValueTask CreateGoalAsync(
        GoalCreateRequest request,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalResult result = await goalService.CreateAsync(request, cancellationToken);
            if (result.Goal is null)
            {
                PublishGoalStatus(result.Error ?? "Goal creation failed.");
                return;
            }

            await ReloadGoalsAsync(
                result.Goal.Id,
                $"Created '{result.Goal.Title}'.",
                cancellationToken);
        }, "Goal creation");

    internal async ValueTask UpdateGoalSettingsAsync(
        GoalSettingsUpdateRequest request,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalResult result = await goalService.UpdateSettingsAsync(request, cancellationToken);
            if (result.Goal is null)
            {
                PublishGoalStatus(result.Error ?? "Goal settings update failed.");
                return;
            }

            await ReloadGoalsAsync(
                result.Goal.Id,
                RemoteSpendPreference.FromGoalBudget(result.Goal.RemoteBudget).Mode switch
                {
                    RemoteSpendMode.Unlimited => "Saved private goal limits with unlimited remote spending.",
                    RemoteSpendMode.Capped => $"Saved explicit remote cap of ${GoalPresentationFormatter.ToUsd(result.Goal.RemoteBudget!.Value)}.",
                    _ => "Saved private goal limits with remote spending disabled.",
                },
                cancellationToken);
        }, "Goal settings update");

    internal async ValueTask ExtendGoalBudgetAsync(
        GoalBudgetExtensionRequest request,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalBudgetExtensionResult result = await goalService.ExtendRemoteBudgetAsync(
                request, cancellationToken);
            if (result.Goal is null || result.Extension is null)
            {
                PublishGoalStatus(result.Error ?? "Remote budget extension failed.");
                return;
            }

            await ReloadGoalsAsync(
                result.Goal.Id,
                $"Increased the explicit remote cap to $" +
                $"{GoalPresentationFormatter.ToUsd(result.Extension.NewBudget.Value)}. " +
                "The extension is durable and does not retry a model call automatically.",
                cancellationToken);
        }, "Remote budget extension");

    internal async ValueTask ProposePlanAsync(
        GoalId goalId,
        string content,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            PlanResult result = await goalService.ProposePlanAsync(
                new(goalId, content),
                cancellationToken);
            if (result.Plan is null)
            {
                PublishGoalStatus(result.Error ?? "Plan proposal failed.");
                return;
            }

            await ReloadGoalsAsync(
                goalId,
                $"Plan revision {result.Plan.Revision.Value} awaits approval.",
                cancellationToken);
        }, "Plan proposal");

    internal async ValueTask DecidePlanAsync(
        GoalId goalId,
        PlanDecision decision,
        string? reason,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            PlanView? plan = await goalService.GetCurrentPlanAsync(goalId, cancellationToken);
            if (plan is null)
            {
                PublishGoalStatus("The selected goal has no current plan.");
                return;
            }

            PlanResult result = await goalService.DecidePlanAsync(
                new(goalId, plan.Id, decision, reason),
                cancellationToken);
            if (result.Goal is null)
            {
                PublishGoalStatus(result.Error ?? "Plan decision failed.");
                return;
            }

            string status = decision is PlanDecision.Approve
                ? $"Approved. Isolated branch: {result.Worktree?.Branch}"
                : "Denied. A revised plan is required.";
            await ReloadGoalsAsync(goalId, status, cancellationToken);
        }, "Plan decision");

    internal async ValueTask DiscoverGoalModelsAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalModelCatalog catalog = await goalModelService.DiscoverAsync(
                goalId,
                cancellationToken);
            IReadOnlyList<GoalModelSelectionView> selections =
                await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    ModelCatalog = catalog,
                    ModelSelections = selections,
                    Status = catalog.Error ?? CatalogStatus(catalog),
                },
            });
        }, "Goal model discovery");

    internal async ValueTask SelectGoalModelAsync(
        GoalId goalId,
        AgentRole role,
        GoalModelCandidate candidate,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalModelSelectionResult result = await goalModelService.SelectAsync(new(
                goalId,
                role,
                candidate.Provider,
                candidate.Model), cancellationToken);
            if (result.Selection is null)
            {
                PublishGoalStatus(result.Error ?? "Model selection failed.");
                return;
            }

            IReadOnlyList<GoalModelSelectionView> selections =
                await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
            RemoteCostReport? cost = await remoteCostService.GetAsync(goalId, cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    ModelSelections = selections,
                    Cost = cost,
                    Status = $"Selected {candidate.Provider.Value}/{candidate.Model.Value} for {role}.",
                },
            });
        }, "Goal model selection");

    internal async ValueTask StartGoalWorkflowAsync(
        GoalId goalId,
        GoalModelCandidate leadModel,
        CancellationToken cancellationToken) =>
        await RunWorkflowAsync(
            goalId,
            token => StartPlanningWithModelAsync(goalId, leadModel, token),
            cancellationToken,
            "Lead planning");

    private async IAsyncEnumerable<GoalWorkflowSnapshot> StartPlanningWithModelAsync(
        GoalId goalId,
        GoalModelCandidate leadModel,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        GoalModelSelectionResult selected = await goalModelService.SelectAsync(new(
            goalId,
            AgentRole.Lead,
            leadModel.Provider,
            leadModel.Model), cancellationToken);
        if (selected.Selection is null)
        {
            throw new InvalidOperationException(selected.Error ?? "Lead model selection failed.");
        }

        IReadOnlyList<GoalModelSelectionView> selections =
            await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with { ModelSelections = selections },
        });
        await foreach (GoalWorkflowSnapshot snapshot in goalWorkflowService.StartPlanningAsync(
                           new(goalId), cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return snapshot;
        }
    }

    internal async ValueTask ResumeGoalWorkflowAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunWorkflowAsync(
            goalId,
            token => goalWorkflowService.ResumeAsync(
                new(goalId),
                token),
            cancellationToken,
            "Production workflow");

    internal async ValueTask RetryGoalWorkflowAsync(
        GoalId goalId,
        GoalWorkflowRetryRole role,
        GoalModelCandidate model,
        GoalRetryGuidance? guidance,
        CancellationToken cancellationToken) =>
        await RunWorkflowAsync(
            goalId,
            token => RetryWithModelAsync(
                goalId, role, model, guidance, token),
            cancellationToken,
            $"{role} retry");

    private async IAsyncEnumerable<GoalWorkflowSnapshot> RetryWithModelAsync(
        GoalId goalId,
        GoalWorkflowRetryRole retryRole,
        GoalModelCandidate model,
        GoalRetryGuidance? guidance,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        AgentRole role = retryRole switch
        {
            GoalWorkflowRetryRole.Lead => AgentRole.Lead,
            GoalWorkflowRetryRole.Implementer => AgentRole.Implementer,
            GoalWorkflowRetryRole.Reviewer => AgentRole.Reviewer,
            _ => throw new ArgumentOutOfRangeException(nameof(retryRole)),
        };
        GoalModelSelectionResult selected = await goalModelService.SelectAsync(new(
            goalId,
            role,
            model.Provider,
            model.Model), cancellationToken);
        if (selected.Selection is null)
        {
            throw new InvalidOperationException(selected.Error ?? "Retry model selection failed.");
        }

        IReadOnlyList<GoalModelSelectionView> selections =
            await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with { ModelSelections = selections },
        });
        await foreach (GoalWorkflowSnapshot snapshot in goalWorkflowService.RetryAsync(
                           new(goalId, retryRole, guidance),
                           cancellationToken).WithCancellation(cancellationToken))
        {
            yield return snapshot;
        }
    }

    internal async ValueTask AbortGoalAsync(
        GoalId goalId,
        GoalAbortReason reason,
        CancellationToken cancellationToken)
    {
        await RunGoalCommandAsync(async () =>
        {
            await goalWorkflowService.AbortAsync(new(goalId, reason), cancellationToken);
            WorkspaceView? active = ActiveWorkspace(Current.Workspaces.Registered);
            if (active is not null)
            {
                selectedGoalsByWorkspace.Remove(active.Id);
            }

            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    Items = Current.Goals.Items.Where(goal => goal.Id != goalId).ToArray(),
                    SelectedGoalId = null,
                    CurrentPlan = null,
                    ModelCatalog = null,
                    ModelSelections = [],
                    Cost = null,
                    Workflow = null,
                    SemanticStatus = null,
                    SemanticRebuild = null,
                    SemanticSearch = null,
                    CommitPreview = null,
                    CommitApproval = null,
                    CapabilityApprovals = [],
                    Status = "Goal aborted. Describe a new goal when ready.",
                },
                ComposerText = string.Empty,
                Error = null,
            });
        }, "Goal abort");
    }

    internal void CancelGoalWorkflow() => workflowExecution?.Cancel();

    internal async ValueTask RefreshSemanticStatusAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            SemanticIndexRequest? request = SemanticRequest(goalId);
            if (request is null)
            {
                PublishGoalStatus("An active workspace is required for semantic context.");
                return;
            }

            SemanticIndexStatusResult result = await semanticIndexService.GetStatusAsync(
                request,
                cancellationToken);
            RemoteCostReport? cost = await remoteCostService.GetAsync(goalId, cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    SemanticStatus = result,
                    Cost = cost,
                    Status = result.Error ?? "Semantic status refreshed without inference.",
                },
            });
        }, "Semantic status inspection");

    internal async ValueTask RebuildSemanticIndexAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunSemanticOperationAsync(
            goalId,
            async (request, token) =>
            {
                SemanticIndexResult result = await semanticIndexService.RebuildAsync(request, token);
                SemanticIndexStatusResult status = await semanticIndexService.GetStatusAsync(request, token);
                Publish(Current with
                {
                    Goals = Current.Goals with
                    {
                        SemanticRebuild = result,
                        SemanticStatus = status,
                        Status = result.Error ??
                                 $"Semantic index ready with {result.Partition?.ChunkCount ?? 0} chunks.",
                    },
                });
            },
            cancellationToken,
            "Semantic rebuild");

    internal async ValueTask SearchSemanticContextAsync(
        GoalId goalId,
        string query,
        CancellationToken cancellationToken) =>
        await RunSemanticOperationAsync(
            goalId,
            async (request, token) =>
            {
                SemanticSearchResult result = await semanticIndexService.SearchAsync(new(
                    request.WorkspaceId,
                    query,
                    MaximumResults: 8,
                    request.RemoteGoalId,
                    request.PrivacyPolicy), token);
                Publish(Current with
                {
                    Goals = Current.Goals with
                    {
                        SemanticSearch = result,
                        Status = result.Error ??
                                 $"Semantic preview returned {result.Matches.Count} match(es).",
                    },
                });
            },
            cancellationToken,
            "Semantic search");

    internal void CancelSemanticOperation() => semanticExecution?.Cancel();

    internal async ValueTask RefreshCommitAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalWorkflowSnapshot? workflow = await goalWorkflowService.GetLatestAsync(
                goalId,
                cancellationToken);
            if (workflow is null)
            {
                PublishGoalStatus("The selected goal has no production run.");
                return;
            }

            GoalCommitApprovalView? approval = await goalAcceptanceService.GetAsync(
                goalId,
                workflow.Id,
                cancellationToken);
            GoalCommitPreviewResult? previewResult = approval is null
                ? await goalAcceptanceService.PreviewAsync(goalId, cancellationToken)
                : null;
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    Workflow = workflow,
                    CommitPreview = previewResult?.Preview,
                    CommitApproval = approval,
                    Status = approval is not null
                        ? $"Commit approval is {approval.State}."
                        : previewResult?.Error ?? "Exact commit preview loaded.",
                },
            });
        }, "Commit preview");

    internal async ValueTask RequestCommitApprovalAsync(
        GoalCommitMessage message,
        GoalCommitAuthorName authorName,
        GoalCommitAuthorEmail authorEmail,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalCommitPreview? preview = Current.Goals.CommitPreview;
            if (preview is null)
            {
                PublishGoalStatus("Load an exact commit preview before recording a request.");
                return;
            }

            GoalCommitApprovalResult result = await goalAcceptanceService.RequestAsync(new(
                preview.GoalId,
                preview.RunId,
                preview.Head,
                preview.DiffHash,
                message,
                authorName,
                authorEmail), cancellationToken);
            if (result.Approval is null)
            {
                PublishGoalStatus(result.Error ?? "Commit approval request failed.");
                return;
            }

            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    CommitPreview = null,
                    CommitApproval = result.Approval,
                    Status = "Exact commit request recorded as Pending. A separate decision is required.",
                },
            });
        }, "Commit approval request");

    internal async ValueTask DecideCommitAsync(
        GoalCommitDecision decision,
        GoalCommitDecisionReason? reason,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalCommitApprovalView? approval = Current.Goals.CommitApproval;
            if (approval is null)
            {
                PublishGoalStatus("No commit approval request is loaded.");
                return;
            }

            GoalCommitApprovalResult result = await goalAcceptanceService.DecideAsync(new(
                approval.Id,
                decision,
                reason), cancellationToken);
            if (result.Approval is null)
            {
                PublishGoalStatus(result.Error ?? "Commit decision failed.");
                return;
            }

            GoalWorkflowSnapshot? workflow = await goalWorkflowService.GetLatestAsync(
                result.Approval.GoalId,
                cancellationToken);
            string status = result.Approval.State switch
            {
                GoalCommitApprovalState.Denied => "Commit denied; no Git commit was created.",
                GoalCommitApprovalState.Approved =>
                    result.Error ?? "Commit remains approved and can be resumed safely.",
                GoalCommitApprovalState.Committed =>
                    $"Committed exact approved diff: {result.Approval.CommitSha?.Value}",
                GoalCommitApprovalState.Pending => "Commit decision remains pending.",
                _ => throw new InvalidOperationException("Unsupported commit approval state."),
            };
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    CommitApproval = result.Approval,
                    Workflow = workflow,
                    Status = status,
                },
            });
        }, "Commit decision");

    internal async ValueTask CreateApplicationBackupAsync(
        BackupDestinationPath destination,
        CancellationToken cancellationToken)
    {
        if (Current.Operations.IsBusy)
        {
            return;
        }

        Publish(Current with
        {
            Operations = Current.Operations with
            {
                IsBusy = true,
                LastBackup = null,
                Status = "Creating and integrity-checking application-state backup…",
            },
        });
        try
        {
            ApplicationBackupResult result = await applicationOperationsService.CreateBackupAsync(
                destination,
                cancellationToken);
            Publish(Current with
            {
                Operations = Current.Operations with
                {
                    LastBackup = result.Backup,
                    Status = result.Error ??
                             $"Verified backup created for schema {result.Backup?.SchemaVersion.Value}.",
                },
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Application-state backup cancelled");
            Publish(Current with
            {
                Operations = Current.Operations with { Status = "Backup cancelled." },
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application-state backup failed");
            Publish(Current with
            {
                Operations = Current.Operations with { Status = exception.Message },
            });
        }
        finally
        {
            Publish(Current with
            {
                Operations = Current.Operations with { IsBusy = false },
            });
        }
    }

    internal async ValueTask InspectApplicationRestoreAsync(
        RestoreSourcePath source,
        CancellationToken cancellationToken)
    {
        if (Current.Operations.IsBusy)
        {
            return;
        }

        Publish(Current with
        {
            Operations = Current.Operations with
            {
                IsBusy = true,
                InspectedRestore = null,
                Status = "Inspecting archive and verifying hashes, SQLite, schema, and layout…",
            }
        });
        try
        {
            ApplicationRestoreInspectionResult result =
                await applicationOperationsService.InspectRestoreAsync(source, cancellationToken);
            Publish(Current with
            {
                Operations = Current.Operations with
                {
                    InspectedRestore = result.Restore,
                    Status = result.Error ?? "Archive verified. Review it before staging restore.",
                }
            });
        }
        catch (OperationCanceledException)
        {
            Publish(Current with
            {
                Operations = Current.Operations with
                { Status = "Restore inspection cancelled." }
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application-state restore inspection failed");
            Publish(Current with
            {
                Operations = Current.Operations with
                { Status = exception.Message }
            });
        }
        finally
        {
            Publish(Current with { Operations = Current.Operations with { IsBusy = false } });
        }
    }

    internal async ValueTask StageApplicationRestoreAsync(
        ApplicationRestoreView restore,
        CancellationToken cancellationToken)
    {
        if (Current.Operations.IsBusy)
        {
            return;
        }

        Publish(Current with
        {
            Operations = Current.Operations with
            { IsBusy = true, Status = "Revalidating and staging restore…" }
        });
        try
        {
            ApplicationRestoreStageResult result =
                await applicationOperationsService.StageRestoreAsync(
                    new(restore.Archive, restore.ArchiveSha256), cancellationToken);
            Publish(Current with
            {
                Operations = Current.Operations with
                {
                    PendingRestore = result.Restore,
                    Status = result.Error ?? (result.RestartRequired
                    ? "Verified restore staged. Restart Harness.NET to apply it."
                    : "Restore was not staged."),
                }
            });
        }
        catch (OperationCanceledException)
        {
            Publish(Current with
            {
                Operations = Current.Operations with
                { Status = "Restore staging cancelled." }
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application-state restore staging failed");
            Publish(Current with
            {
                Operations = Current.Operations with
                { Status = exception.Message }
            });
        }
        finally
        {
            Publish(Current with { Operations = Current.Operations with { IsBusy = false } });
        }
    }

    internal async ValueTask RefreshCapabilityApprovalsAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            CapabilityApprovalSnapshot result = await capabilityApprovalService.ListAsync(
                goalId.Value,
                cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    CapabilityApprovals = result.Items,
                    Status = result.Error ?? $"{result.Items.Count} restore approval(s).",
                },
            });
        }, "Restore approval refresh");

    internal async ValueTask RequestRestoreApprovalAsync(
        GoalId goalId,
        ToolCorrelationId correlationId,
        string rationale,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            CapabilityApprovalResult result = await capabilityApprovalService.RequestAsync(new(
                goalId.Value,
                correlationId,
                CapabilityKind.Restore,
                rationale), cancellationToken);
            if (result.Approval is null)
            {
                PublishGoalStatus(result.Error ?? "Restore approval request failed.");
                return;
            }

            await ReloadCapabilityApprovalsAsync(
                goalId,
                "Restore request recorded as Pending. A separate decision is required.",
                cancellationToken);
        }, "Restore approval request");

    internal async ValueTask DecideRestoreApprovalAsync(
        GoalId goalId,
        CapabilityApprovalId approvalId,
        CapabilityDecision decision,
        string? reason,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            CapabilityApprovalResult result = await capabilityApprovalService.DecideAsync(new(
                approvalId,
                decision,
                reason), cancellationToken);
            if (result.Approval is null)
            {
                PublishGoalStatus(result.Error ?? "Restore approval decision failed.");
                return;
            }

            await ReloadCapabilityApprovalsAsync(
                goalId,
                decision is CapabilityDecision.Approve
                    ? "Approved exactly one correlated restore request."
                    : "Restore request denied.",
                cancellationToken);
        }, "Restore approval decision");

    public void Dispose()
    {
        submission?.Cancel();
        submission?.Dispose();
        workflowExecution?.Cancel();
        workflowExecution?.Dispose();
        semanticExecution?.Cancel();
        semanticExecution?.Dispose();
        commandGate.Dispose();
        states.Dispose();
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

    private async ValueTask RunWorkflowAsync(
        GoalId goalId,
        Func<CancellationToken, IAsyncEnumerable<GoalWorkflowSnapshot>> operation,
        CancellationToken cancellationToken,
        string operationName)
    {
        if (Current.Goals.IsBusy || Current.Goals.IsWorkflowRunning)
        {
            return;
        }

        workflowExecution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with
            {
                IsBusy = true,
                IsWorkflowRunning = true,
                Status = $"{operationName} started.",
            },
        });
        try
        {
            await foreach (GoalWorkflowSnapshot snapshot in operation(workflowExecution.Token)
                               .WithCancellation(workflowExecution.Token))
            {
                RemoteCostReport? cost = await remoteCostService.GetAsync(
                    goalId,
                    workflowExecution.Token);
                Publish(Current with
                {
                    Goals = Current.Goals with
                    {
                        Workflow = snapshot,
                        Cost = cost,
                        Status = WorkflowStatus(snapshot),
                    },
                });
            }

            await ReloadGoalsAsync(
                goalId,
                Current.Goals.Workflow is null
                    ? $"{operationName} returned no workflow snapshot."
                    : WorkflowStatus(Current.Goals.Workflow),
                workflowExecution.Token);
        }
        catch (OperationCanceledException) when (workflowExecution.IsCancellationRequested)
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
            workflowExecution.Dispose();
            workflowExecution = null;
            Publish(Current with
            {
                Goals = Current.Goals with { IsBusy = false, IsWorkflowRunning = false },
            });
        }
    }

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
