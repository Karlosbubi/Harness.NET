using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Workspaces;
using Harness.BusinessLogic.Workflows;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed class MainWindow : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly HarnessThemeController themeController;
    private readonly IRunOutputService runOutputService;
    private readonly IWorkbenchInspectionService inspectionService;
    private readonly IWorkbenchDocumentService documentService;
    private readonly IWorkbenchCodeIntelligenceService codeIntelligenceService;
    private readonly IWorkspaceMutationService mutationService;
    private readonly IWorkbenchLayoutService layoutService;
    private readonly CancellationToken cancellationToken;
    private readonly CompositeDisposable subscriptions = new();
    private readonly ItemsControl activities = new();
    private readonly ScrollViewer conversationScroll = new();
    private readonly TextBox composer = new();
    private readonly StatusIndicator status = new();
    private readonly TextBlock budget = new();
    private readonly TextBlock workspace = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock goalContext = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ItemsControl evidence = new();
    private readonly ComboBox modelPicker = new();
    private readonly Button send = new() { Content = "Send" };
    private readonly Button cancel = new() { Content = "Cancel" };
    private readonly Button openWorkspace = new() { Content = "Open workspace" };
    private readonly Button manageWorkspaces = new() { Content = "Workspaces…" };
    private readonly Button manageFramework = new() { Content = "Framework" };
    private readonly Button inspectGoalContext = new() { Content = "Inspect semantic context…" };
    private readonly Button operations = new() { Content = "Operations" };
    private readonly Button settings = new() { Content = "Settings" };
    private readonly Button commandBar = new()
    {
        Classes = { "command-bar" },
        MaxWidth = 460,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly AccessibleIconButton refreshProvider = new()
    {
        Content = "↻",
        AccessibleName = "Refresh provider models",
    };
    private readonly AccessibleIconButton openSettings = new()
    {
        Content = "⚙",
        AccessibleName = "Open Settings",
    };
    private readonly Border header = new();
    private readonly Border navigation = new();
    private readonly Border primary = new();
    private readonly Border utility = new();
    private readonly Border footer = new();
    private readonly TextBlock brandDetail = new()
    {
        Text = "Agent workspace for .NET",
        FontSize = 11,
    };
    private readonly TextBlock modelLabel = new()
    {
        Text = "Chat",
        VerticalAlignment = VerticalAlignment.Center,
    };
    private WorkbenchDockHost? workbench;
    private bool suppressSelection;
    private bool loaded;
    private bool closingAfterLayoutSave;

    internal MainWindow(
        AvaloniaPresentationStore store,
        HarnessThemeController themeController,
        IRunOutputService runOutputService,
        IWorkbenchInspectionService inspectionService,
        IWorkbenchDocumentService documentService,
        IWorkbenchCodeIntelligenceService codeIntelligenceService,
        IWorkspaceMutationService mutationService,
        IWorkbenchLayoutService layoutService,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.themeController = themeController;
        this.runOutputService = runOutputService;
        this.inspectionService = inspectionService;
        this.documentService = documentService;
        this.codeIntelligenceService = codeIntelligenceService;
        this.mutationService = mutationService;
        this.layoutService = layoutService;
        this.cancellationToken = cancellationToken;
        Title = "Harness.NET";
        Width = 1280;
        Height = 800;
        MinWidth = 800;
        MinHeight = 600;
        Content = BuildContent();
        WireInteractions();
        SizeChanged += (_, _) =>
        {
            UpdateResponsiveChrome(Bounds.Width);
            workbench?.ApplyViewport(Bounds.Width, Bounds.Height);
        };

        subscriptions.Add(store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state))));
        subscriptions.Add(themeController.Snapshots.Subscribe(_ =>
            Dispatcher.UIThread.Post(() =>
            {
                ApplyTheme();
                RenderActivities(store.Current);
            })));
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += (_, _) => subscriptions.Dispose();
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new("Auto,*,28"),
        };
        navigation.Child = BuildNavigation();
        primary.Child = BuildPrimary();
        utility.Child = BuildUtility();
        workbench = new(
            runOutputService,
            inspectionService,
            documentService,
            codeIntelligenceService,
            layoutService,
            new AvaloniaWorkbenchDocumentPrompt(),
            () => store.Current,
            navigation,
            primary,
            utility,
            cancellationToken,
            ShowWorkspaceDialogAsync,
            mutationService);
        Border documentActions = new()
        {
            Child = workbench.DocumentActions,
            Padding = new(12, 6),
            BorderThickness = new(0, 0, 0, 1),
        };
        Grid workbenchRegion = new()
        {
            RowDefinitions = new("Auto,*"),
            Children = { documentActions },
        };
        Grid.SetRow(workbench.Control, 1);
        workbenchRegion.Children.Add(workbench.Control);
        Grid.SetRow(workbenchRegion, 1);
        root.Children.Add(workbenchRegion);

        header.Child = BuildHeader();
        header.MinHeight = 56;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        footer.Child = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private Control BuildHeader()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new("Auto,Auto,*,Auto"),
            Margin = new(14, 8),
            ColumnSpacing = 16,
        };

        Border mark = new()
        {
            Classes = { "app-mark" },
            Child = new TextBlock { Text = "H" },
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);

        StackPanel titleText = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Harness.NET", FontSize = 15, FontWeight = FontWeight.SemiBold },
                brandDetail,
            },
        };
        StackPanel title = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { mark, titleText },
        };
        grid.Children.Add(title);

        openWorkspace.Classes.Add("primary");
        openWorkspace.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(openWorkspace, "Open workspace folder");
        Grid.SetColumn(openWorkspace, 1);
        grid.Children.Add(openWorkspace);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Cluster(modelLabel, modelPicker, refreshProvider),
                Cluster(openSettings),
            },
        };
        Grid.SetColumn(commandBar, 2);
        grid.Children.Add(commandBar);

        ScrollViewer actionScroller = new()
        {
            Content = actions,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(actionScroller, "Workbench commands");
        Grid.SetColumn(actionScroller, 3);
        grid.Children.Add(actionScroller);
        AutomationProperties.SetName(modelPicker, "Conversation model");
        modelPicker.MinWidth = 120;
        modelPicker.Classes.Add("toolbar-input");
        refreshProvider.Classes.Add("icon");
        openSettings.Classes.Add("icon");
        modelLabel.Classes.Add("cluster-label");
        return grid;
    }

    /// <summary>The command bar states its own shortcut so the palette is discoverable.</summary>
    private Control BuildCommandBar()
    {
        Grid content = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "Search commands",
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Border shortcut = new()
        {
            Classes = { "kbd" },
            Child = new TextBlock { Text = "Ctrl+Shift+P" },
        };
        shortcut.SetValue(Grid.ColumnProperty, 1);
        content.Children.Add(shortcut);
        return content;
    }

    /// <summary>Groups a label with the control it names so the header reads as one affordance.</summary>
    private static Border Cluster(params Control[] children)
    {
        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (Control child in children)
        {
            row.Children.Add(child);
        }

        return new Border
        {
            Classes = { "cluster" },
            Child = row,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void UpdateResponsiveChrome(double width)
    {
        bool compact = width > 0 && width < 1024;
        brandDetail.IsVisible = !compact;
        openWorkspace.IsVisible = !compact;
        // The palette keeps its keyboard shortcut when the bar is hidden for width.
        commandBar.IsVisible = !compact;
        modelLabel.IsVisible = !compact;
    }

    private Control BuildNavigation()
    {
        StackPanel panel = new()
        {
            Margin = new(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "WORKSPACE", FontSize = 11, FontWeight = FontWeight.Bold },
                workspace,
                manageWorkspaces,
                new Separator(),
                new TextBlock { Text = "COLLABORATE", FontSize = 11, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "●  Conversation", TextWrapping = TextWrapping.Wrap },
                manageFramework,
                new Separator(),
                new TextBlock { Text = "APPLICATION", FontSize = 11, FontWeight = FontWeight.Bold },
                settings,
                operations,
            },
        };
        foreach (Button button in new[] { manageWorkspaces, manageFramework, settings, operations })
        {
            button.Classes.Add("command");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
        }
        AutomationProperties.SetName(manageWorkspaces, "Manage workspaces");
        AutomationProperties.SetName(manageFramework, "Engineering framework");
        AutomationProperties.SetName(settings, "Application settings");
        AutomationProperties.SetName(operations, "Application operations");
        AutomationProperties.SetName(panel, "Workspace navigation");
        return panel;
    }

    private Control BuildPrimary()
    {
        Grid grid = new()
        {
            RowDefinitions = new("*,Auto"),
            Margin = new(1, 0),
        };
        activities.Margin = new(4, 0);
        AutomationProperties.SetName(activities, "Conversation activity");
        conversationScroll.Content = activities;
        conversationScroll.Margin = new(12, 10, 12, 0);
        grid.Children.Add(conversationScroll);

        Grid composerArea = new()
        {
            ColumnDefinitions = new("*,Auto,Auto"),
            Margin = new(12),
            ColumnSpacing = 8,
        };
        composer.AcceptsReturn = true;
        composer.TextWrapping = TextWrapping.Wrap;
        composer.MinHeight = 64;
        composer.PlaceholderText = "Message the local model";
        AutomationProperties.SetName(composer, "Goal or message composer");
        composerArea.Children.Add(composer);
        Grid.SetColumn(send, 1);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        send.Classes.Add("primary");
        AutomationProperties.SetName(send, "Submit composer");
        composerArea.Children.Add(send);
        Grid.SetColumn(cancel, 2);
        cancel.VerticalAlignment = VerticalAlignment.Bottom;
        cancel.Classes.Add("command");
        AutomationProperties.SetName(cancel, "Cancel current response");
        composerArea.Children.Add(cancel);
        Grid.SetRow(composerArea, 1);
        grid.Children.Add(composerArea);
        return grid;
    }

    private Control BuildUtility()
    {
        AutomationProperties.SetName(evidence, "Selected goal evidence");
        StackPanel panel = new()
        {
            Margin = new(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "CURRENT GOAL", Classes = { "eyebrow" } },
                goalContext,
                inspectGoalContext,
                new Separator(),
                new TextBlock { Text = "RECENT EVIDENCE", Classes = { "eyebrow" } },
                evidence,
            },
        };
        inspectGoalContext.Classes.Add("command");
        AutomationProperties.SetName(inspectGoalContext, "Inspect selected goal semantic context");
        AutomationProperties.SetName(panel, "Goal context and evidence details");
        return new ScrollViewer { Content = panel };
    }

    private Control BuildFooter()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new("*,Auto"),
            Margin = new(10, 3),
        };
        AutomationProperties.SetName(status, "Application status");
        grid.Children.Add(status);
        Grid.SetColumn(budget, 1);
        grid.Children.Add(budget);
        return grid;
    }

    private void WireInteractions()
    {
        subscriptions.Add(composer.GetObservable(TextBox.TextProperty).Subscribe(value =>
        {
            if (value != store.Current.ComposerText)
            {
                store.SetComposerText(value ?? string.Empty);
            }
        }));
        composer.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key is Key.Enter && eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                eventArgs.Handled = true;
                await store.SubmitComposerAsync(cancellationToken);
            }
            else if (eventArgs.Key is Key.Escape && store.Current.IsStreaming)
            {
                store.CancelSubmission();
            }
        };
        send.Click += async (_, _) => await store.SubmitComposerAsync(cancellationToken);
        cancel.Click += (_, _) => store.CancelSubmission();
        modelPicker.SelectionChanged += async (_, _) =>
        {
            if (!suppressSelection && modelPicker.SelectedItem is string model)
            {
                await store.SelectModelAsync(model, cancellationToken);
            }
        };
        refreshProvider.Click +=
            async (_, _) => await store.RefreshProviderAsync(cancellationToken);
        openSettings.Click += async (_, _) => await ShowSettingsAsync();
        openWorkspace.Click += async (_, _) => await ShowWorkspaceDialogAsync(true);
        manageWorkspaces.Click += async (_, _) => await ShowWorkspaceDialogAsync(false);
        inspectGoalContext.Click += async (_, _) => await ShowSemanticContextAsync();
        manageFramework.Click += async (_, _) =>
        {
            FrameworkDialog dialog = new(store, cancellationToken);
            await dialog.ShowDialog(this);
        };
        settings.Click += async (_, _) => await ShowSettingsAsync();
        operations.Click += async (_, _) =>
        {
            OperationsDialog dialog = new(store, cancellationToken);
            await dialog.ShowDialog(this);
        };
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
        commandBar.Content = BuildCommandBar();
        AutomationProperties.SetName(commandBar, "Open the command palette");
        commandBar.Click += async (_, _) => await ShowCommandPaletteAsync();
    }

    private async void OnShellKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && args.Key is Key.P)
        {
            args.Handled = true;
            await ShowCommandPaletteAsync();
        }
        else if (args.KeyModifiers == KeyModifiers.Control && args.Key is Key.P)
        {
            args.Handled = true;
            await ShowQuickOpenAsync();
        }
        else if (args.KeyModifiers == KeyModifiers.Control && args.Key is Key.OemComma)
        {
            args.Handled = true;
            await ShowSettingsAsync();
        }
    }

    internal async Task ShowQuickOpenAsync()
    {
        if (workbench is not { } host)
        {
            return;
        }

        IReadOnlyList<PaletteCommand> files = await host.BuildFileCommandsAsync();
        if (files.Count == 0)
        {
            // Say why nothing is offered instead of opening an empty picker.
            status.Severity = StatusSeverity.Warning;
            status.Message = "Open and trust a workspace to search its tracked files.";
            return;
        }

        CommandPaletteDialog picker = new(files, "Go to file", "Type a file name");
        await picker.ShowDialog(this);
    }

    private async Task ShowCommandPaletteAsync()
    {
        CommandPaletteDialog palette = new(BuildCommands());
        await palette.ShowDialog(this);
    }

    /// <summary>
    /// Describes the commands the shell can actually run right now. A command that needs
    /// an active or trusted workspace is listed with the reason instead of being hidden.
    /// </summary>
    internal IReadOnlyList<PaletteCommand> BuildCommands()
    {
        AvaloniaShellState state = store.Current;
        WorkspaceView? active = state.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        string? needsWorkspace = active is null ? "Open a workspace first" : null;
        string? needsTrust = active is null
            ? "Open a workspace first"
            : active.IsTrusted ? null : "Trust the workspace first";

        List<PaletteCommand> commands =
        [
            new("workspace.open", "Workspace", "Open workspace…",
                () => new(ShowWorkspaceDialogAsync(true))),
            new("workspace.manage", "Workspace", "Manage workspaces…",
                () => new(ShowWorkspaceDialogAsync(false))),
            new("goal.context", "Goal", "Inspect semantic context…",
                () => new(ShowSemanticContextAsync()),
                UnavailableReason: state.Goals.SelectedGoal is null
                    ? "Create or continue a goal first"
                    : needsTrust),
            new("framework.manage", "Framework", "Effective framework…",
                () => new(ShowDialogAsync(new FrameworkDialog(store, cancellationToken))),
                UnavailableReason: needsWorkspace),
            new("settings.open", "Application", "Settings…",
                () => new(ShowSettingsAsync()), "Ctrl+,"),
            new("operations.manage", "Application", "Operations and backup…",
                () => new(ShowDialogAsync(new OperationsDialog(store, cancellationToken)))),
            new("provider.refresh", "Providers", "Refresh provider health",
                async () => await store.RefreshProviderAsync(cancellationToken)),
            new("themes.reload", "Appearance", "Reload user themes",
                async () => await store.RefreshThemesAsync(cancellationToken)),
        ];

        if (workbench is { } host)
        {
            commands.AddRange(
            [
                new("tool.files", "Panels", "Show Files panel",
                    () => { host.ShowFiles(); return ValueTask.CompletedTask; }, "Ctrl+Shift+E"),
                new("tool.git", "Panels", "Show Git panel",
                    () => { host.ShowGit(); return ValueTask.CompletedTask; }, "Ctrl+Shift+G"),
                new("tool.output", "Panels", "Show Run output panel",
                    () => { host.ShowRunOutput(); return ValueTask.CompletedTask; }, "Ctrl+J"),
                new("tool.problems", "Panels", "Show Problems panel",
                    () => { host.ShowProblems(); return ValueTask.CompletedTask; }, "Ctrl+Shift+M"),
                new("git.diff", "Git", "Open working-tree diff",
                    async () => await host.OpenDiffAsync(), UnavailableReason: needsTrust),
                new("layout.save", "Layout", "Save workbench layout",
                    async () => await host.SaveLayoutAsync()),
                new("layout.reset", "Layout", "Reset workbench layout",
                    async () => await host.ResetLayoutAsync()),
            ]);
        }

        return commands;
    }

    private async Task ShowDialogAsync(Window dialog) => await dialog.ShowDialog(this);

    private async Task ShowSettingsAsync() =>
        await new SettingsWindow(store, cancellationToken).ShowDialog(this);

    private async Task ShowSemanticContextAsync()
    {
        if (store.Current.Goals.SelectedGoal is not { } goal)
        {
            return;
        }

        await store.RefreshSemanticStatusAsync(goal.Id, cancellationToken);
        await new SemanticContextDialog(store, goal, cancellationToken).ShowDialog(this);
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        themeController.Attach(this);
        await store.LoadAsync(cancellationToken);
        if (workbench is not null)
        {
            await workbench.RestoreLayoutAsync();
            await workbench.RefreshAsync();
        }
        if (store.Current.Workspaces.Registered.Any(item => item.IsActive))
        {
            composer.Focus();
        }
        else
        {
            openWorkspace.Focus();
        }
    }

    private async Task ShowWorkspaceDialogAsync(bool browseImmediately)
    {
        WorkspaceDialog dialog = new(
            store,
            cancellationToken,
            browseOnOpen: browseImmediately,
            prepareWorkspaceChange: PrepareWorkspaceChangeAsync);
        await dialog.ShowDialog(this);
    }

    private async Task<bool> PrepareWorkspaceChangeAsync() =>
        workbench is null || await workbench.PrepareForWorkspaceChangeAsync();

    private async void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (closingAfterLayoutSave || workbench is null)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (!await workbench.PrepareForShutdownAsync())
        {
            return;
        }

        await workbench.SaveLayoutAsync(CancellationToken.None);
        closingAfterLayoutSave = true;
        Close();
    }

    private void Render(AvaloniaShellState state)
    {
        suppressSelection = true;
        try
        {
            composer.Text = state.ComposerText;
            composer.IsEnabled = !state.IsLoading;
            bool createsGoal = state.Goals.SelectedGoal is null;
            composer.PlaceholderText = createsGoal
                ? "Describe the goal you want Harness to pursue"
                : "Message Harness about the selected goal";
            send.Content = createsGoal ? "Create goal" : "Send";
            send.IsEnabled = !state.IsLoading && !state.IsStreaming &&
                             !string.IsNullOrWhiteSpace(state.ComposerText);
            cancel.IsVisible = state.IsStreaming;
            manageFramework.IsEnabled = !state.IsLoading &&
                                        state.Workspaces.Registered.Any(item => item.IsActive);
            inspectGoalContext.IsEnabled = !state.IsLoading && state.Goals.SelectedGoal is not null;
            DashboardSnapshot? dashboard = state.Dashboard;
            if (dashboard is not null)
            {
                bool hasWorkspace = state.Workspaces.Registered.Any(item => item.IsActive);
                workspace.Text = hasWorkspace
                    ? $"{dashboard.Workspace.Name}\n{dashboard.Workspace.Branch}\n{dashboard.Workspace.Trust}"
                    : "No workspace open\nChoose a Git-backed .NET repository to begin.";
                brandDetail.Text = hasWorkspace
                    ? $"{dashboard.Workspace.Name} · {dashboard.Workspace.Branch}"
                    : "No workspace open";
                manageWorkspaces.Content = hasWorkspace ? "Switch workspace…" : "Open workspace…";
                RenderActivities(state);
                ToolTip.SetTip(modelPicker, ProviderText(dashboard.Provider));
                RenderGoalInspector(state.Goals);
                string[] models = dashboard.Provider.Models.Select(model => model.Id).ToArray();
                modelPicker.ItemsSource = models;
                modelPicker.SelectedItem = models.FirstOrDefault(model =>
                    model == dashboard.Provider.SelectedModel);
                status.Message = state.Error is not null
                    ? $"Error: {state.Error}"
                    : state.IsStreaming ? "Streaming response" : dashboard.Status;
                status.Severity = state.Error is not null
                    ? StatusSeverity.Error
                    : state.IsStreaming ? StatusSeverity.Information : StatusSeverity.Success;
                budget.Text = dashboard.Budget;
            }
            else
            {
                status.Message = state.Error ?? "Loading";
                status.Severity = state.Error is null
                    ? StatusSeverity.Information
                    : StatusSeverity.Error;
            }

            if (state.Appearance is { } appearance)
            {
                themeController.Register(AvaloniaThemeMapper.UserThemes(appearance));
                themeController.Select(new(appearance.EffectiveThemeId.Value));
            }
            workbench?.Update(state);
        }
        finally
        {
            suppressSelection = false;
        }

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        header.Background = Brush(UiThemeColorToken.Header);
        header.BorderBrush = Brush(UiThemeColorToken.Border);
        header.BorderThickness = new Thickness(0, 0, 0, 1);
        navigation.Background = Brush(UiThemeColorToken.Panel);
        primary.Background = Brush(UiThemeColorToken.Editor);
        utility.Background = Brush(UiThemeColorToken.Panel);
        footer.Background = Brush(UiThemeColorToken.Header);
        footer.BorderBrush = Brush(UiThemeColorToken.Border);
        footer.BorderThickness = new Thickness(0, 1, 0, 0);
        Background = Brush(UiThemeColorToken.Window);
        Foreground = Brush(UiThemeColorToken.TextPrimary);
        status.RefreshTheme();
    }

    private void RenderActivities(AvaloniaShellState state)
    {
        List<Control> timeline = state.Dashboard?.Activities
            .Select(CreateMessageCard)
            .ToList() ?? [];
        if (state.Goals.SelectedGoal is null && state.Goals.Items.Count > 0)
        {
            timeline.Add(new TextBlock
            {
                Text = "CONTINUE A GOAL",
                Classes = { "eyebrow" },
                Margin = new Thickness(6, 8, 6, 6),
            });
            timeline.AddRange(state.Goals.Items.Select(CreateGoalChoice));
        }
        IReadOnlyList<ConversationWorkflowCard> workflow =
            ConversationWorkflowProjector.Project(state.Goals, state.Error);
        if (workflow.Count > 0)
        {
            timeline.Add(new TextBlock
            {
                Text = "GOAL TIMELINE",
                Classes = { "eyebrow" },
                Margin = new Thickness(6, 8, 6, 6),
            });
            timeline.AddRange(workflow.Select(CreateWorkflowCard));
        }
        activities.ItemsSource = timeline;
        Dispatcher.UIThread.Post(conversationScroll.ScrollToEnd);
    }

    private Control CreateGoalChoice(GoalView goal)
    {
        Button select = new()
        {
            Content = "Continue",
            Classes = { "command" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(select, $"Continue goal {goal.Title}");
        select.Click += async (_, _) => await store.SelectGoalAsync(goal.Id, cancellationToken);

        Grid heading = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        heading.Children.Add(new TextBlock
        {
            Text = goal.Title,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(select, 1);
        heading.Children.Add(select);
        Border card = new()
        {
            Classes = { "workflow-card" },
            Margin = new Thickness(4, 0, 28, 9),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    heading,
                    new TextBlock
                    {
                        Text = goal.Objective,
                        TextWrapping = TextWrapping.Wrap,
                        MaxHeight = 52,
                    },
                },
            },
        };
        AutomationProperties.SetName(card, $"Available goal: {goal.Title}, {goal.State}");
        AutomationProperties.SetAccessibilityView(card, AccessibilityView.Content);
        return card;
    }

    private Control CreateWorkflowCard(ConversationWorkflowCard item)
    {
        Border stateBadge = new()
        {
            Classes = { "workflow-state" },
            Child = new TextBlock { Text = item.State.ToString().ToUpperInvariant() },
        };
        stateBadge.Classes.Add(item.State switch
        {
            ConversationWorkflowCardState.Approved or ConversationWorkflowCardState.Completed or
                ConversationWorkflowCardState.Recovered => "success",
            ConversationWorkflowCardState.Denied or ConversationWorkflowCardState.Failed or
                ConversationWorkflowCardState.Cancelled or ConversationWorkflowCardState.Stale => "attention",
            _ => "neutral",
        });
        Grid heading = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        heading.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(stateBadge, 1);
        heading.Children.Add(stateBadge);
        StackPanel content = new()
        {
            Spacing = 6,
            Children =
            {
                heading,
                new TextBlock { Text = item.Summary, TextWrapping = TextWrapping.Wrap },
            },
        };
        if (item.Details is { Length: > 0 } details && details != item.Summary)
        {
            content.Children.Add(new TextBlock
            {
                Text = details,
                Classes = { "muted" },
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 86,
            });
        }

        IReadOnlyList<ConversationWorkflowAction> actions =
            ConversationWorkflowActionProjector.Project(item, store.Current.Goals);
        if (actions.Count > 0)
        {
            StackPanel actionRow = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0),
            };
            foreach (ConversationWorkflowAction action in actions)
            {
                Button button = new() { Content = action.Label };
                button.Classes.Add(action.IsPrimary ? "primary" : "command");
                button.IsEnabled = !store.Current.Goals.IsBusy &&
                                   !store.Current.Goals.IsWorkflowRunning;
                AutomationProperties.SetName(button, action.Label);
                button.Click += async (_, _) => await ExecuteWorkflowActionAsync(action.Kind, item);
                actionRow.Children.Add(button);
            }
            content.Children.Add(actionRow);
        }

        Border card = new()
        {
            Classes = { "workflow-card" },
            Child = content,
            Margin = new Thickness(4, 0, 28, 9),
        };
        AutomationProperties.SetName(
            card,
            $"{item.Kind}: {item.Title}, {item.State}");
        AutomationProperties.SetAccessibilityView(card, AccessibilityView.Content);
        return card;
    }

    private async Task ExecuteWorkflowActionAsync(
        ConversationWorkflowActionKind action,
        ConversationWorkflowCard card)
    {
        GoalView? goal = store.Current.Goals.SelectedGoal;
        if (goal is null)
        {
            return;
        }

        switch (action)
        {
            case ConversationWorkflowActionKind.ConfigureGoal:
                await new GoalSettingsDialog(store, goal, cancellationToken).ShowDialog(this);
                break;
            case ConversationWorkflowActionKind.StartPlanning:
                await StartPlanningAsync(goal);
                break;
            case ConversationWorkflowActionKind.WritePlan:
                await WritePlanAsync(goal);
                break;
            case ConversationWorkflowActionKind.ApprovePlan:
                await ApprovePlanAsync(goal);
                break;
            case ConversationWorkflowActionKind.RequestPlanChanges:
                await RequestPlanChangesAsync(goal);
                break;
            case ConversationWorkflowActionKind.ContinueRun:
                await ContinueRunAsync(goal);
                break;
            case ConversationWorkflowActionKind.CancelRun:
                store.CancelGoalWorkflow();
                break;
            case ConversationWorkflowActionKind.ReviewAcceptedChanges:
                await store.RefreshCommitAsync(goal.Id, cancellationToken);
                break;
            case ConversationWorkflowActionKind.ApproveRestore:
                await ApproveRestoreAsync(goal, card);
                break;
            case ConversationWorkflowActionKind.DenyRestore:
                await DenyRestoreAsync(goal, card);
                break;
            case ConversationWorkflowActionKind.ReviewCommitPreview:
                await new CommitApprovalDialog(store, cancellationToken).ShowDialog(this);
                break;
            case ConversationWorkflowActionKind.ApproveCommit:
                await DecideCommitAsync(resuming: false);
                break;
            case ConversationWorkflowActionKind.DenyCommit:
                await DenyCommitAsync();
                break;
            case ConversationWorkflowActionKind.ResumeCommit:
                await DecideCommitAsync(resuming: true);
                break;
        }
    }

    private async Task StartPlanningAsync(GoalView goal)
    {
        OutputLimitsDialog dialog = new(
            "Generate goal plan",
            ["Lead maximum output tokens"],
            GoalPresentationFormatter.StartDisclosure(store.Current.Goals),
            [DefaultOutputMaximum(AgentRole.Lead)]);
        await dialog.ShowDialog(this);
        if (dialog.Result is { Length: 1 } limits)
        {
            await store.StartGoalWorkflowAsync(
                goal.Id,
                new(limits[0]),
                cancellationToken);
        }
    }

    private async Task WritePlanAsync(GoalView goal)
    {
        TextEntryDialog dialog = new(
            "Write plan manually",
            "Plan content",
            "Save plan",
            "A plan is required.");
        await dialog.ShowDialog(this);
        if (dialog.Result is { } content)
        {
            await store.ProposePlanAsync(goal.Id, content, cancellationToken);
        }
    }

    private async Task ApprovePlanAsync(GoalView goal)
    {
        if (store.Current.Goals.CurrentPlan is not { } plan)
        {
            return;
        }

        PlanApprovalDialog dialog = new(goal, plan);
        if (await dialog.ShowDialog<bool>(this))
        {
            await store.DecidePlanAsync(
                goal.Id,
                PlanDecision.Approve,
                reason: null,
                cancellationToken);
        }
    }

    private async Task RequestPlanChangesAsync(GoalView goal)
    {
        TextEntryDialog dialog = new(
            "Request plan changes",
            "Required reason",
            "Request changes",
            "A reason is required.");
        await dialog.ShowDialog(this);
        if (dialog.Result is { } reason)
        {
            await store.DecidePlanAsync(
                goal.Id,
                PlanDecision.Deny,
                reason,
                cancellationToken);
        }
    }

    private async Task ContinueRunAsync(GoalView goal)
    {
        OutputLimitsDialog dialog = new(
            "Continue production run",
            ["Implementer maximum output tokens", "Reviewer maximum output tokens"],
            GoalPresentationFormatter.ResumeDisclosure(goal, store.Current.Goals),
            [
                DefaultOutputMaximum(AgentRole.Implementer),
                DefaultOutputMaximum(AgentRole.Reviewer),
            ]);
        await dialog.ShowDialog(this);
        if (dialog.Result is { Length: 2 } limits)
        {
            await store.ResumeGoalWorkflowAsync(
                goal.Id,
                new(limits[0]),
                new(limits[1]),
                cancellationToken);
        }
    }

    private int DefaultOutputMaximum(AgentRole role) =>
        store.Current.Settings.AgentDefaults?.Roles
            .FirstOrDefault(item => item.Role == role)
            ?.MaximumOutputTokens.Value ?? 2048;

    private async Task ApproveRestoreAsync(GoalView goal, ConversationWorkflowCard card)
    {
        CapabilityApprovalView? approval = RestoreApproval(card);
        if (approval is null)
        {
            return;
        }

        RestoreDecisionConfirmationDialog confirmation = new(approval);
        if (await confirmation.ShowDialog<bool>(this))
        {
            await store.DecideRestoreApprovalAsync(
                goal.Id,
                approval.Id,
                CapabilityDecision.Approve,
                reason: null,
                cancellationToken);
        }
    }

    private async Task DenyRestoreAsync(GoalView goal, ConversationWorkflowCard card)
    {
        CapabilityApprovalView? approval = RestoreApproval(card);
        if (approval is null)
        {
            return;
        }

        TextEntryDialog dialog = new(
            "Deny restore request",
            "Required reason",
            "Deny request",
            "A denial reason is required.");
        await dialog.ShowDialog(this);
        if (dialog.Result is { } reason)
        {
            await store.DecideRestoreApprovalAsync(
                goal.Id,
                approval.Id,
                CapabilityDecision.Deny,
                reason,
                cancellationToken);
        }
    }

    private CapabilityApprovalView? RestoreApproval(ConversationWorkflowCard card) =>
        store.Current.Goals.CapabilityApprovals.FirstOrDefault(approval =>
            card.Id == $"capability.{approval.Id.Value}" &&
            approval.Capability is CapabilityKind.Restore &&
            approval.State is CapabilityApprovalState.Pending);

    private async Task DecideCommitAsync(bool resuming)
    {
        if (store.Current.Goals.CommitApproval is not { } approval)
        {
            return;
        }

        ExactCommitConfirmationDialog confirmation = new(approval, resuming);
        if (await confirmation.ShowDialog<bool>(this))
        {
            await store.DecideCommitAsync(
                GoalCommitDecision.Approve,
                reason: null,
                cancellationToken);
        }
    }

    private async Task DenyCommitAsync()
    {
        TextEntryDialog dialog = new(
            "Deny exact commit",
            "Required reason",
            "Deny commit",
            "A denial reason is required.");
        await dialog.ShowDialog(this);
        if (dialog.Result is { } reason)
        {
            await store.DecideCommitAsync(
                GoalCommitDecision.Deny,
                new GoalCommitDecisionReason(reason),
                cancellationToken);
        }
    }

    private Control CreateMessageCard(ActivityItem item)
    {
        bool isUser = string.Equals(item.Actor, "You", StringComparison.Ordinal);
        TextBlock actor = new()
        {
            Text = item.Actor,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
        };
        Control content = MarkdownContentView.Create(item.Summary, Brush);
        content.Margin = new Thickness(0, 5, 0, 0);
        TextBlock messageStatus = new()
        {
            Text = item.Status,
            FontSize = 11,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid metadata = new() { ColumnDefinitions = new("*,Auto") };
        metadata.Children.Add(actor);
        Grid.SetColumn(messageStatus, 1);
        metadata.Children.Add(messageStatus);
        StackPanel body = new() { Children = { metadata, content } };
        Border card = new()
        {
            Child = body,
            Padding = new Thickness(13, 10),
            Margin = isUser ? new Thickness(52, 0, 4, 10) : new Thickness(4, 0, 52, 10),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
        };
        card.Classes.Add("message-card");
        if (isUser)
        {
            card.Classes.Add("user");
        }

        return card;
    }

    private void RenderGoalInspector(GoalManagementState goals)
    {
        if (goals.SelectedGoal is not { } selected)
        {
            goalContext.Text = "No goal selected\nOpen Goals and plans to select or create one.";
            evidence.ItemsSource = Array.Empty<Control>();
            return;
        }

        string plan = goals.CurrentPlan is null
            ? "No plan proposed"
            : $"Plan revision {goals.CurrentPlan.Revision.Value} · {goals.CurrentPlan.State}";
        string workflow = goals.Workflow is null
            ? "No workflow started"
            : $"Workflow {goals.Workflow.State} · {goals.Workflow.Tasks.Count} task(s)";
        goalContext.Text = $"{selected.Title}\n{selected.State}\n\n{selected.Objective}\n\n" +
                           $"{plan}\n{workflow}";

        evidence.ItemsSource = goals.Workflow?.Evidence.Count > 0
            ? goals.Workflow.Evidence
                .Select(item => CreateEvidenceCard(item.Title.Value, item.Content.Value))
                .ToArray()
            : new Control[]
            {
                new TextBlock
                {
                    Text = "No durable workflow evidence exists for this goal yet.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                },
            };
    }

    private Control CreateEvidenceCard(string title, string content) => new Border
    {
        Padding = new Thickness(10, 8),
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(7),
        Background = Brush(UiThemeColorToken.Raised),
        BorderBrush = Brush(UiThemeColorToken.Border),
        BorderThickness = new Thickness(1),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                MarkdownContentView.Create(content, Brush),
            },
        },
    };

    private static string ProviderText(ProviderSnapshot provider)
    {
        string selected = string.IsNullOrWhiteSpace(provider.SelectedModel)
            ? "No model selected"
            : provider.SelectedModel;
        string catalog = provider.Models.Count == 0
            ? "No models discovered"
            : $"{provider.Models.Count} model(s) available";
        return $"{provider.Name}\n{provider.Health}\n{catalog}\nSelected: {selected}" +
               (provider.Error is null ? string.Empty : $"\n{provider.Error}");
    }

    private static IBrush? Brush(UiThemeColorToken token) =>
        Application.Current?.TryFindResource(HarnessThemeResources.Key(token), out object? value) is true
            ? value as IBrush
            : null;

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> items = [];

        internal void Add(IDisposable disposable) => items.Add(disposable);

        public void Dispose()
        {
            foreach (IDisposable item in items)
            {
                item.Dispose();
            }

            items.Clear();
        }
    }
}
