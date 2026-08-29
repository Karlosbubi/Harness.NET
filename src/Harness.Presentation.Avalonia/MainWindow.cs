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
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed partial class MainWindow : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly HarnessThemeController themeController;
    private readonly IRunOutputService runOutputService;
    private readonly IWorkbenchInspectionService inspectionService;
    private readonly IDeveloperGitService developerGitService;
    private readonly IWorkbenchDocumentService documentService;
    private readonly IWorkbenchCodeIntelligenceService codeIntelligenceService;
    private readonly IWorkspaceMutationService mutationService;
    private readonly IWorkbenchLayoutService layoutService;
    private readonly IProjectUserSecretsService projectUserSecretsService;
    private readonly IDeveloperProjectExecutionService developerExecutionService;
    private readonly CancellationToken cancellationToken;
    private readonly CompositeDisposable subscriptions = new();
    private readonly WorkbenchEventSurface workbenchEvents;
    private readonly ItemsControl activities = new();
    private readonly ScrollViewer conversationScroll = new();
    private readonly TextBox composer = new();
    private readonly StatusIndicator status = new();
    private readonly TextBlock budget = new();
    private readonly TextBlock workspace = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock goalContext = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ItemsControl evidence = new();
    private readonly AutoCompleteBox modelPicker = new()
    {
        MinimumPrefixLength = 0,
        MinimumPopulateDelay = TimeSpan.Zero,
        MaxDropDownHeight = 360,
        IsTextCompletionEnabled = false,
        FilterMode = AutoCompleteFilterMode.Contains,
        PlaceholderText = "Search models",
    };
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
    private readonly Button showConversation = new() { Content = "Chat" };
    private readonly Button inboundMcpIndicator = new() { Content = "MCP ACTIVE", IsVisible = false };
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
    private WorkbenchDockHost? workbench;
    private bool suppressSelection;
    private bool loaded;
    private bool closingAfterLayoutSave;
    private static readonly KeybindingCommand[] ShellKeyCommands =
    [
        KeybindingCommand.ShowCommandPalette,
        KeybindingCommand.QuickOpen,
        KeybindingCommand.OpenSettings,
        KeybindingCommand.ShowChat,
        KeybindingCommand.ShowFiles,
        KeybindingCommand.ShowGit,
        KeybindingCommand.ShowRunOutput,
        KeybindingCommand.ShowProblems,
        KeybindingCommand.FocusNextRegion,
    ];

    internal MainWindow(
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
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.themeController = themeController;
        this.runOutputService = runOutputService;
        this.inspectionService = inspectionService;
        this.developerGitService = developerGitService;
        this.documentService = documentService;
        this.codeIntelligenceService = codeIntelligenceService;
        this.mutationService = mutationService;
        this.layoutService = layoutService;
        this.projectUserSecretsService = projectUserSecretsService;
        this.developerExecutionService = developerExecutionService;
        this.cancellationToken = cancellationToken;
        agentActivityStatus = new(toolEvidenceService, agentActivityReader);
        workbenchEvents = new(NavigateToWorkbenchEvent);
        agentActivityStatus.CancelRequested += store.CancelGoalWorkflow;
        agentActivityStatus.GoalRequested += ShowConversation;
        agentActivityStatus.EvidenceRequested += () => workbench?.OpenEvidence();
        store.WorkbenchEventPublished += OnWorkbenchEventPublished;
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
        Closed += OnClosed;
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
            mutationService,
            ShowProjectUserSecretsAsync,
            developerExecutionService,
            developerGitService,
            () => store.RefreshActiveWorkspaceContextAsync(cancellationToken).AsTask(),
            ShowWorkspaceDialogAtAsync);
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
        Grid.SetRowSpan(workbenchEvents.Control, 3);
        workbenchEvents.Control.SetValue(Panel.ZIndexProperty, 100);
        root.Children.Add(workbenchEvents.Control);
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
                inboundMcpIndicator,
                agentActivityStatus.Control,
                Cluster(showConversation, modelPicker, refreshProvider),
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
        showConversation.Classes.Add("command");
        AutomationProperties.SetName(showConversation, "Show Conversation panel");
        inboundMcpIndicator.Classes.Add("command");
        AutomationProperties.SetName(inboundMcpIndicator, "Inbound MCP server active; open control settings");
        inboundMcpIndicator.Click += async (_, _) => await ShowSettingsAsync();
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
            Child = new TextBlock
            {
                Text = (store.Current.Settings.KeybindingSettings ??
                        KeybindingSettingsSnapshot.Default)
                    .DisplayFor(KeybindingCommand.ShowCommandPalette),
            },
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
        send.Click += async (_, _) =>
        {
            store.SetComposerText(composer.Text ?? string.Empty);
            await store.SubmitComposerAsync(cancellationToken);
        };
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
        showConversation.Click += (_, _) => ShowConversation();
        openWorkspace.Click += async (_, _) => await ShowWorkspaceDialogAsync(true);
        manageWorkspaces.Click += async (_, _) => await ShowWorkspaceDialogAsync(false);
        inspectGoalContext.Click += async (_, _) => await ShowSemanticContextAsync();
        manageFramework.Click += async (_, _) =>
        {
            FrameworkDialog dialog = new(store, cancellationToken);
            await dialog.ShowDialog(this);
        };
        settings.Click += async (_, _) => await ShowSettingsAsync();
        operations.Click += async (_, _) => await ShowOperationsAsync();
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
        commandBar.Content = BuildCommandBar();
        AutomationProperties.SetName(commandBar, "Open the command palette");
        commandBar.Click += async (_, _) => await ShowCommandPaletteAsync();
    }

    private async void OnShellKeyDown(object? sender, KeyEventArgs args)
    {
        KeybindingSettingsSnapshot bindings = store.Current.Settings.KeybindingSettings ??
                                              KeybindingSettingsSnapshot.Default;
        KeybindingCommand? command = KeybindingInput.Match(args, bindings, ShellKeyCommands);
        if (command is null) return;
        args.Handled = true;
        switch (command.Value)
        {
            case KeybindingCommand.ShowCommandPalette:
                await ShowCommandPaletteAsync();
                break;
            case KeybindingCommand.QuickOpen:
                await ShowQuickOpenAsync();
                break;
            case KeybindingCommand.OpenSettings:
                await ShowSettingsAsync();
                break;
            case KeybindingCommand.ShowChat:
                ShowConversation();
                break;
            case KeybindingCommand.ShowFiles:
                workbench?.ShowFiles();
                break;
            case KeybindingCommand.ShowGit:
                workbench?.ShowGit();
                break;
            case KeybindingCommand.ShowRunOutput:
                workbench?.ShowRunOutput();
                break;
            case KeybindingCommand.ShowProblems:
                workbench?.ShowProblems();
                break;
            case KeybindingCommand.FocusNextRegion:
                workbench?.FocusNextRegion();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
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
}
