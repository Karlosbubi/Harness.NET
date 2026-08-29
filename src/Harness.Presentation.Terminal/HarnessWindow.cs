using System.Collections.ObjectModel;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Terminal.Gui.App;
using Terminal.Gui.Editor;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Harness.Presentation.Terminal;

internal sealed partial class HarnessWindow : Window
{
    private const int WorkspaceWidth = 26;
    private const int ComposerHeight = 3;
    private const int FooterHeight = 1;

    private readonly IApplication application;
    private readonly IDashboardService dashboardService;
    private readonly IWorkspaceService workspaceService;
    private readonly IFrameworkService frameworkService;
    private readonly IGoalService goalService;
    private readonly IRemoteCostService remoteCostService;
    private readonly IGoalModelService goalModelService;
    private readonly IGoalWorkflowService goalWorkflowService;
    private readonly IGoalAcceptanceService goalAcceptanceService;
    private readonly ISemanticIndexService semanticIndexService;
    private readonly IApplicationOperationsService operationsService;
    private readonly AgentDefaultsSnapshot agentDefaults;
    private readonly CancellationToken cancellationToken;
    private readonly FrameView workspaceFrame;
    private readonly FrameView activityFrame;
    private readonly FrameView detailsFrame;
    private readonly Label workspaceText;
    private readonly Button manageWorkspaces;
    private readonly Button trustWorkspace;
    private readonly MenuItem trustWorkspaceMenuItem;
    private readonly Label activityText;
    private readonly Label detailsText;
    private readonly TextField modelSearch;
    private readonly ListView modelList;
    private readonly Button refreshModels;
    private readonly Button useModel;
    private readonly TextField composer;
    private readonly Button send;
    private readonly Label status;
    private string[] selectableModelIds = [];
    private WorkspaceView? activeWorkspace;
    private IReadOnlyList<GoalView> goals;
    private DashboardSnapshot latestSnapshot;

    internal HarnessWindow(
        IApplication application,
        IDashboardService dashboardService,
        IWorkspaceService workspaceService,
        IFrameworkService frameworkService,
        IGoalService goalService,
        IRemoteCostService remoteCostService,
        IGoalModelService goalModelService,
        IGoalWorkflowService goalWorkflowService,
        IGoalAcceptanceService goalAcceptanceService,
        ISemanticIndexService semanticIndexService,
        IApplicationOperationsService operationsService,
        AgentDefaultsSnapshot agentDefaults,
        DashboardSnapshot initialSnapshot,
        WorkspaceView? activeWorkspace,
        IReadOnlyList<GoalView> goals,
        CancellationToken cancellationToken)
    {
        this.application = application;
        this.dashboardService = dashboardService;
        this.workspaceService = workspaceService;
        this.frameworkService = frameworkService;
        this.goalService = goalService;
        this.remoteCostService = remoteCostService;
        this.goalModelService = goalModelService;
        this.goalWorkflowService = goalWorkflowService;
        this.goalAcceptanceService = goalAcceptanceService;
        this.semanticIndexService = semanticIndexService;
        this.operationsService = operationsService;
        this.agentDefaults = agentDefaults;
        this.activeWorkspace = activeWorkspace;
        this.goals = goals;
        latestSnapshot = initialSnapshot;
        this.cancellationToken = cancellationToken;
        Title = "Harness.NET";

        workspaceText = CreateContentLabel();
        activityText = CreateContentLabel();
        detailsText = CreateContentLabel();

        workspaceFrame = CreateFrame("Workspace", workspaceText);
        workspaceText.Height = Dim.Fill(3);
        manageWorkspaces = new()
        {
            Title = "_Manage",
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = 11,
        };
        trustWorkspace = new()
        {
            Title = "_Trust",
            X = Pos.Right(manageWorkspaces) + 1,
            Y = Pos.AnchorEnd(2),
            Width = 10,
            Enabled = activeWorkspace is { IsTrusted: false },
        };
        manageWorkspaces.Accepted += async (_, _) => await ManageWorkspacesAsync();
        trustWorkspace.Accepted += async (_, _) => await TrustWorkspaceAsync();
        workspaceFrame.Add(manageWorkspaces, trustWorkspace);

        MenuItem manageWorkspacesMenuItem = new(
            "_Manage",
            Key.Empty,
            () => _ = ManageWorkspacesAsync());
        trustWorkspaceMenuItem = new(
            "_Trust",
            Key.Empty,
            () => _ = TrustWorkspaceAsync())
        {
            Enabled = activeWorkspace is { IsTrusted: false },
        };
        MenuBar menuBar = new(
        [
            new MenuBarItem("_Workspace", [manageWorkspacesMenuItem, trustWorkspaceMenuItem]),
            new MenuBarItem("_Framework",
            [
                new MenuItem("_Inspect effective", Key.Empty, () => _ = InspectFrameworkAsync()),
                new MenuItem("_Edit private overlay", Key.Empty, () => _ = EditPrivateOverlayAsync()),
            ]),
            new MenuBarItem("_Goals",
            [
                new MenuItem("_Manage goals and plans", Key.Empty, () => _ = ManageGoalsAsync()),
            ]),
            new MenuBarItem("_Operations",
            [
                new MenuItem("Create _backup", Key.Empty, () => _ = CreateBackupAsync()),
                new MenuItem("_Restore backup", Key.Empty, () => _ = RestoreBackupAsync()),
            ]),
        ]);
        activityFrame = CreateFrame("Activity", activityText);
        detailsFrame = CreateFrame("Provider", detailsText);
        detailsText.Height = Dim.Fill(8);
        modelSearch = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(7),
            Width = Dim.Fill(),
            Height = 1,
        };
        modelList = new ListView
        {
            X = 0,
            Y = Pos.AnchorEnd(5),
            Width = Dim.Fill(),
            Height = 2,
        };
        refreshModels = new Button
        {
            Title = "_Refresh",
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = 12,
        };
        useModel = new Button
        {
            Title = "_Use model",
            X = Pos.Right(refreshModels) + 1,
            Y = Pos.AnchorEnd(2),
            Width = 14,
        };
        refreshModels.Accepted += async (_, _) => await RefreshProviderAsync();
        useModel.Accepted += async (_, _) => await SelectModelAsync();
        modelSearch.Accepting += (_, args) =>
        {
            args.Handled = true;
            RenderProviderModels(latestSnapshot);
        };
        detailsFrame.Add(modelSearch, modelList, refreshModels, useModel);

        composer = new TextField
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(12),
            Height = 1,
        };
        composer.Accepted += async (_, _) => await SubmitAsync();
        send = new()
        {
            Title = "_Send",
            X = Pos.AnchorEnd(10),
            Y = 0,
            Width = 10,
        };
        send.Accepted += async (_, _) => await SubmitAsync();

        FrameView composerFrame = new()
        {
            Title = "Instruction",
            X = 0,
            Y = Pos.AnchorEnd(ComposerHeight + FooterHeight),
            Width = Dim.Fill(),
            Height = ComposerHeight,
        };
        composerFrame.Add(composer, send);

        status = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(FooterHeight),
            Width = Dim.Fill(),
            Height = FooterHeight,
        };

        Add(menuBar, workspaceFrame, activityFrame, detailsFrame, composerFrame, status);
        Render(initialSnapshot);
        ViewportChanged += (_, _) => ApplyLayout(Viewport.Width);
        Initialized += (_, _) => composer.SetFocus();
    }

    private async Task SubmitAsync()
    {
        string instruction = composer.Text?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return;
        }

        try
        {
            send.Enabled = false;
            composer.ReadOnly = true;
            await foreach (DashboardSnapshot snapshot in dashboardService
                               .SubmitAsync(instruction, cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                application.Invoke(() => Render(snapshot));
            }

            application.Invoke(() => composer.Text = string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Submission failed | {exception.Message}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                application.Invoke(() =>
                {
                    send.Enabled = true;
                    composer.ReadOnly = false;
                    composer.SetFocus();
                });
            }
        }
    }

    private void Render(DashboardSnapshot snapshot)
    {
        latestSnapshot = snapshot;
        RenderWorkspace(snapshot);

        activityText.Text = string.Join(
            "\n\n",
            snapshot.Activities.Select(item =>
                $"{item.Actor} [{item.Status}]\n{item.Summary}"));
        int transcriptLines = activityText.Text?.ToString()?.Count(character => character == '\n') + 1 ?? 1;
        activityText.SetContentHeight(transcriptLines);
        activityText.VerticalScrollBar.Value = Math.Max(0, transcriptLines - activityText.Viewport.Height);

        detailsText.Text = $"{snapshot.Provider.Name}: {snapshot.Provider.Health}\n" +
                           $"Selected: {snapshot.Provider.SelectedModel}\n" +
                           $"Discovered: {snapshot.Provider.Models.Count}" +
                           (snapshot.Provider.Error is null
                               ? string.Empty
                               : $"\n{snapshot.Provider.Error}");

        RenderProviderModels(snapshot);

        status.Text = $"{snapshot.Status} | {snapshot.Budget}";
    }

    private void RenderProviderModels(DashboardSnapshot snapshot)
    {
        string search = modelSearch.Text?.ToString()?.Trim() ?? string.Empty;
        ProviderModel[] visible = snapshot.Provider.Models
            .Where(model => search.Length == 0 ||
                $"{model.Id} {string.Join(' ', model.Capabilities)}"
                    .Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        selectableModelIds = visible.Select(model => model.Id).ToArray();
        string[] modelLabels = visible.Select(model =>
            $"{(model.Id == snapshot.Provider.SelectedModel ? "*" : " ")} {model.Id}  " +
            string.Join(',', model.Capabilities)).ToArray();
        ObservableCollection<string> models = new(modelLabels);
        modelList.SetSource(models);
        int selectedModel = Array.IndexOf(selectableModelIds, snapshot.Provider.SelectedModel);
        if (selectedModel >= 0)
        {
            modelList.SelectedItem = selectedModel;
        }
    }

    private void RenderWorkspace(DashboardSnapshot snapshot)
    {
        if (activeWorkspace is null)
        {
            workspaceFrame.Title = "Workspace";
            workspaceText.Text = string.Join('\n',
                "No workspace selected",
                "Use Workspace > Manage to register a repository.");
            trustWorkspace.Enabled = false;
            trustWorkspaceMenuItem.Enabled = false;
            return;
        }

        workspaceFrame.Title = $"Active · {activeWorkspace.Name}";
        workspaceText.Text = string.Join('\n',
            activeWorkspace.Name,
            activeWorkspace.RootPath,
            string.Empty,
            $"Entry   {Path.GetFileName(activeWorkspace.EntryPoint)}",
            $"Branch  {activeWorkspace.Branch}",
            $"State   {(activeWorkspace.IsDirty ? "dirty" : "clean")}",
            $"Trust   {(activeWorkspace.IsTrusted ? "trusted" : "untrusted")}",
            string.Empty,
            GoalTextFormatter.FormatCompact(goals));
        trustWorkspace.Enabled = !activeWorkspace.IsTrusted;
        trustWorkspaceMenuItem.Enabled = !activeWorkspace.IsTrusted;
    }

    private async Task ManageWorkspacesAsync()
    {
        try
        {
            IReadOnlyList<WorkspaceView> registered =
                await workspaceService.ListAsync(cancellationToken);
            using WorkspaceDialog dialog = new(
                application,
                workspaceService,
                registered,
                activeWorkspace?.RootPath ?? latestSnapshot.Workspace.Path,
                cancellationToken);
            await application.RunAsync(dialog, cancellationToken);
            if (dialog.Result is not null)
            {
                activeWorkspace = dialog.Result;
                goals = await goalService.ListAsync(activeWorkspace.Id, cancellationToken);
                application.Invoke(() => RenderWorkspace(latestSnapshot));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Workspace command failed | {exception.Message}");
        }
    }

    private async Task ManageGoalsAsync()
    {
        if (activeWorkspace is null)
        {
            MessageBox.Query(
                application,
                "Goals",
                "Select a workspace before managing goals.",
                "_Close");
            return;
        }

        try
        {
            goals = await goalService.ListAsync(activeWorkspace.Id, cancellationToken);
            using GoalDialog dialog = new(
                application,
                goalService,
                remoteCostService,
                goalModelService,
                goalWorkflowService,
                goalAcceptanceService,
                semanticIndexService,
                agentDefaults,
                activeWorkspace.Id,
                goals,
                cancellationToken);
            await application.RunAsync(dialog, cancellationToken);
            goals = dialog.Goals;
            application.Invoke(() => RenderWorkspace(latestSnapshot));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Goal command failed | {exception.Message}");
        }
    }

    private async Task TrustWorkspaceAsync()
    {
        if (activeWorkspace is null || activeWorkspace.IsTrusted)
        {
            return;
        }

        int? choice = MessageBox.Query(
            application,
            "Trust workspace",
            $"Trust '{activeWorkspace.Name}' for local build/test and code-intelligence project " +
            "evaluation, including configured analyzers and source generators?",
            "_Trust",
            "_Cancel");
        if (choice != 0)
        {
            return;
        }

        try
        {
            WorkspaceResult result = await workspaceService.SetTrustAsync(
                activeWorkspace.Id,
                isTrusted: true,
                cancellationToken);
            activeWorkspace = result.Workspace;
            application.Invoke(() => RenderWorkspace(latestSnapshot));
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Trust failed | {exception.Message}");
        }
    }

    private async Task InspectFrameworkAsync()
    {
        if (activeWorkspace is null)
        {
            MessageBox.Query(
                application,
                "Framework",
                "Select a workspace before inspecting its effective framework.",
                "_Close");
            return;
        }

        try
        {
            FrameworkSnapshot snapshot = await frameworkService.GetEffectiveAsync(
                activeWorkspace.Id,
                activeWorkspace.RootPath,
                cancellationToken);
            using Dialog dialog = new()
            {
                Title = "Effective framework",
                Width = Dim.Percent(90),
                Height = Dim.Percent(85),
            };
            Editor content = new()
            {
                Text = FrameworkTextFormatter.Format(snapshot),
                ReadOnly = true,
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar |
                                   ViewportSettingsFlags.HasHorizontalScrollBar,
            };
            dialog.Add(content);
            dialog.AddButton(new Button { Title = "_Close" });
            await application.RunAsync(dialog, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Framework failed | {exception.Message}");
        }
    }

    private async Task EditPrivateOverlayAsync()
    {
        if (activeWorkspace is null)
        {
            MessageBox.Query(
                application,
                "Framework",
                "Select a workspace before editing its private overlay.",
                "_Close");
            return;
        }

        try
        {
            FrameworkSnapshot snapshot = await frameworkService.GetEffectiveAsync(
                activeWorkspace.Id,
                activeWorkspace.RootPath,
                cancellationToken);
            string current = snapshot.Documents
                .FirstOrDefault(document => document.Layer == "private-workspace")
                ?.Content ?? string.Empty;
            using Dialog dialog = new()
            {
                Title = "Private workspace overlay",
                Width = Dim.Percent(90),
                Height = Dim.Percent(85),
            };
            Editor editor = new()
            {
                Text = current,
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar,
            };
            dialog.Add(editor);
            dialog.AddButton(new Button { Title = "_Cancel" });
            dialog.AddButton(new Button { Title = "_Save" });
            await application.RunAsync(dialog, cancellationToken);
            if (dialog.Result != 1)
            {
                return;
            }

            await frameworkService.SetPrivateOverlayAsync(
                activeWorkspace.Id,
                activeWorkspace.RootPath,
                editor.Text?.ToString(),
                cancellationToken);
            application.Invoke(() => status.Text = "Private framework overlay updated");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Framework failed | {exception.Message}");
        }
    }

    private async Task RefreshProviderAsync() => await RunProviderCommandAsync(
        token => dashboardService.RefreshProviderAsync(token));

    private async Task SelectModelAsync()
    {
        int selected = modelList.SelectedItem ?? -1;
        if (selected < 0 || selected >= selectableModelIds.Length)
        {
            status.Text = "No model selected";
            return;
        }

        string model = selectableModelIds[selected];
        await RunProviderCommandAsync(token => dashboardService.SelectModelAsync(model, token));
    }

    private async Task RunProviderCommandAsync(
        Func<CancellationToken, ValueTask<DashboardSnapshot>> command)
    {
        try
        {
            refreshModels.Enabled = false;
            useModel.Enabled = false;
            DashboardSnapshot snapshot = await command(cancellationToken);
            application.Invoke(() => Render(snapshot));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Provider command failed | {exception.Message}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                application.Invoke(() =>
                {
                    refreshModels.Enabled = true;
                    useModel.Enabled = true;
                });
            }
        }
    }

    private void ApplyLayout(int width)
    {
        ShellLayout layout = ShellLayoutPolicy.ForWidth(width);
        workspaceFrame.Visible = layout.ShowWorkspace;
        detailsFrame.Visible = layout.ShowDetails;

        workspaceFrame.X = 0;
        workspaceFrame.Width = WorkspaceWidth;

        activityFrame.X = layout.ShowWorkspace ? Pos.Right(workspaceFrame) : 0;
        activityFrame.Width = layout.ShowDetails ? Dim.Percent(50) - WorkspaceWidth : Dim.Fill();

        detailsFrame.X = Pos.Right(activityFrame);
        detailsFrame.Width = Dim.Fill();
    }

    private static FrameView CreateFrame(string title, View content)
    {
        FrameView frame = new()
        {
            Title = title,
            Y = 1,
            Height = Dim.Fill(ComposerHeight + FooterHeight),
        };
        frame.Add(content);
        return frame;
    }

    private static Label CreateContentLabel() => new()
    {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        CanFocus = true,
        ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar,
    };
}
