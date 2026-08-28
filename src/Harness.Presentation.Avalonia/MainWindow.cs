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
using Harness.BusinessLogic.Events;
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
        agentActivityStatus = new(toolEvidenceService);
        workbenchEvents = new(NavigateToWorkbenchEvent);
        agentActivityStatus.CancelRequested += store.CancelGoalWorkflow;
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
        KeybindingSettingsSnapshot bindings = state.Settings.KeybindingSettings ??
                                              KeybindingSettingsSnapshot.Default;
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
            new("workspace.user-secrets", "Workspace", "Manage project User Secrets…",
                () => new(ShowProjectUserSecretsAsync()),
                UnavailableReason: needsTrust,
                MatchText: "Workspace Project User Secrets credentials development dotnet"),
            new("workspace.quick.open", "Workspace", "Go to file…",
                () => new(ShowQuickOpenAsync()),
                bindings.DisplayFor(KeybindingCommand.QuickOpen),
                UnavailableReason: needsTrust),
            new("goal.context", "Goal", "Inspect semantic context…",
                () => new(ShowSemanticContextAsync()),
                UnavailableReason: state.Goals.SelectedGoal is null
                    ? "Create or continue a goal first"
                    : needsTrust),
            new("framework.manage", "Framework", "Effective framework…",
                () => new(ShowDialogAsync(new FrameworkDialog(store, cancellationToken))),
                UnavailableReason: needsWorkspace),
            new("settings.open", "Application", "Settings…",
                () => new(ShowSettingsAsync()), bindings.DisplayFor(KeybindingCommand.OpenSettings)),
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
                    () => { host.ShowFiles(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowFiles)),
                new("tool.conversation", "Panels", "Show Chat panel",
                    () => { ShowConversation(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowChat),
                    MatchText: "Panels Show Chat Conversation goal agent message"),
                new("tool.git", "Panels", "Show Git panel",
                    () => { host.ShowGit(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowGit)),
                new("tool.output", "Panels", "Show Run output panel",
                    () => { host.ShowRunOutput(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowRunOutput)),
                new("tool.problems", "Panels", "Show Problems panel",
                    () => { host.ShowProblems(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowProblems)),
                new("git.diff", "Git", "Open working-tree diff",
                    async () => await host.OpenDiffAsync(), UnavailableReason: needsTrust),
                EditorCommand("editor.save", "Save document", KeybindingCommand.SaveDocument,
                    "Open an editable document first"),
                EditorCommand("editor.close", "Close document", KeybindingCommand.CloseDocument,
                    "Open a source document first"),
                EditorCommand("editor.completion", "Show completion", KeybindingCommand.ShowCompletion,
                    "Open a C# document first"),
                EditorCommand("editor.quick.info", "Show quick info", KeybindingCommand.ShowQuickInfo,
                    "Open a C# document first"),
                EditorCommand("editor.definition", "Go to definition", KeybindingCommand.GoToDefinition,
                    "Open a C# document first"),
                EditorCommand("editor.references", "Find references", KeybindingCommand.FindReferences,
                    "Open a C# document first"),
                EditorCommand("editor.implementations", "Find implementations",
                    KeybindingCommand.FindImplementations, "Open a C# document first"),
                EditorCommand("editor.rename", "Rename symbol", KeybindingCommand.RenameSymbol,
                    "Open an editable C# document first"),
                new("editor.format.document", "Editor", "Format document",
                    async () => await host.InvokeActiveEditorCommandAsync(
                        KeybindingCommand.FormatDocument),
                    bindings.DisplayFor(KeybindingCommand.FormatDocument),
                    UnavailableReason: host.CanInvokeActiveEditorCommand(KeybindingCommand.FormatDocument)
                            ? null : "Open an editable C# document first"),
                new("editor.format.selection", "Editor", "Format selection",
                    async () => await host.InvokeActiveEditorCommandAsync(
                        KeybindingCommand.FormatSelection),
                    bindings.DisplayFor(KeybindingCommand.FormatSelection),
                    UnavailableReason: host.CanInvokeActiveEditorCommand(KeybindingCommand.FormatSelection)
                            ? null : "Select code in an editable C# document first"),
                new("editor.format.changed", "Editor", "Format changed code",
                    async () => await host.TransformActiveDocumentAsync(
                        WorkbenchCodeDocumentTransformationKind.FormatChangedSpans),
                    UnavailableReason: host.CanTransformActiveDocument(
                        WorkbenchCodeDocumentTransformationKind.FormatChangedSpans)
                            ? null : "Open an editable C# document first"),
                new("editor.organize.imports", "Editor", "Organize imports",
                    async () => await host.InvokeActiveEditorCommandAsync(
                        KeybindingCommand.OrganizeImports),
                    bindings.DisplayFor(KeybindingCommand.OrganizeImports),
                    UnavailableReason: host.CanInvokeActiveEditorCommand(KeybindingCommand.OrganizeImports)
                            ? null : "Open an editable C# document first"),
                new("editor.remove.unused.imports", "Editor", "Remove unused imports",
                    async () => await host.TransformActiveDocumentAsync(
                        WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports),
                    UnavailableReason: host.CanTransformActiveDocument(
                        WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports)
                            ? null : "Open an editable C# document first"),
                new("editor.quick.fix", "Editor", "Show quick fixes",
                    async () => await host.InvokeActiveEditorCommandAsync(
                        KeybindingCommand.ShowQuickFixes),
                    bindings.DisplayFor(KeybindingCommand.ShowQuickFixes),
                    UnavailableReason: host.CanInvokeActiveEditorCommand(KeybindingCommand.ShowQuickFixes)
                            ? null : "Open an editable C# document first"),
                new("layout.save", "Layout", "Save workbench layout",
                    async () => await host.SaveLayoutAsync()),
                new("layout.reset", "Layout", "Reset workbench layout",
                    async () => await host.ResetLayoutAsync()),
                new("accessibility.focus.next", "Accessibility", "Focus next workbench region",
                    () => { host.FocusNextRegion(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.FocusNextRegion)),
            ]);

            PaletteCommand EditorCommand(
                string id,
                string title,
                KeybindingCommand command,
                string unavailable) => new(
                id, "Editor", title,
                async () => await host.InvokeActiveEditorCommandAsync(command),
                bindings.DisplayFor(command),
                host.CanInvokeActiveEditorCommand(command) ? null : unavailable);
        }

        return commands;
    }

    private void ShowConversation()
    {
        if (workbench?.ShowConversation() is true)
        {
            Dispatcher.UIThread.Post(() => composer.Focus());
        }
    }

    private async Task ShowDialogAsync(Window dialog) => await dialog.ShowDialog(this);

    private async Task ShowSettingsAsync() =>
        await new SettingsWindow(store, cancellationToken).ShowDialog(this);

    private async Task ShowOperationsAsync() =>
        await new OperationsDialog(store, cancellationToken).ShowDialog(this);

    private void OnWorkbenchEventPublished(WorkbenchEvent workbenchEvent) =>
        Dispatcher.UIThread.Post(() => workbenchEvents.Publish(workbenchEvent));

    private void NavigateToWorkbenchEvent(WorkbenchEventNavigationTarget target)
    {
        switch (target)
        {
            case WorkbenchEventNavigationTarget.Conversation:
                ShowConversation();
                break;
            case WorkbenchEventNavigationTarget.Git:
                workbench?.ShowGit();
                break;
            case WorkbenchEventNavigationTarget.RunOutput:
                workbench?.ShowRunOutput();
                break;
            case WorkbenchEventNavigationTarget.Problems:
                workbench?.ShowProblems();
                break;
            case WorkbenchEventNavigationTarget.Operations:
                _ = ShowOperationsAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    private async Task ShowProjectUserSecretsAsync()
    {
        WorkspaceView? active = store.Current.Workspaces.Registered
            .FirstOrDefault(workspace => workspace.IsActive);
        if (active is null || !active.IsTrusted)
        {
            status.Severity = StatusSeverity.Warning;
            status.Message = active is null
                ? "Open a workspace before managing project User Secrets."
                : "Trust the workspace before managing project User Secrets.";
            return;
        }

        await new ProjectUserSecretsDialog(
            projectUserSecretsService,
            new WorkspaceId(active.Id),
            cancellationToken).ShowDialog(this);
    }

    internal async ValueTask<InboundUiActionResult> ActivateInboundUiAsync(InboundUiActionId action)
    {
        bool applied = action.Value switch
        {
            "chat.show" => ShowConversationForInbound(),
            "panel.files" => workbench?.ShowFiles() == true,
            "panel.git" => workbench?.ShowGit() == true,
            "panel.problems" => workbench?.ShowProblems() == true,
            "panel.output" => workbench?.ShowRunOutput() == true,
            "settings.open" => await OpenSettingsForInboundAsync(),
            _ => false,
        };
        return applied
            ? new(action, true, null, null)
            : new(action, false, "ui_action_unavailable", "The allowlisted Harness action is unavailable.");
    }

    internal ValueTask<InboundUiActionResult> OpenInboundDocumentAsync(
        InboundUiDocumentRequest request) => workbench is null
        ? ValueTask.FromResult(new InboundUiActionResult(new("document.open"), false,
            "workbench_unavailable", "The workbench is unavailable."))
        : workbench.OpenInboundDocumentAsync(request);

    internal IReadOnlyList<InboundOpenDocumentView> InboundOpenDocuments =>
        workbench?.InboundOpenDocuments ?? [];

    private bool ShowConversationForInbound()
    {
        ShowConversation();
        return true;
    }

    private async ValueTask<bool> OpenSettingsForInboundAsync()
    {
        await ShowSettingsAsync();
        return true;
    }

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

    private async Task ShowWorkspaceDialogAtAsync(string path)
    {
        store.SetRepositoryPath(path);
        await ShowWorkspaceDialogAsync(browseImmediately: false);
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
        commandBar.Content = BuildCommandBar();
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
            agentActivityStatus.Update(state.Goals);
            inboundMcpIndicator.IsVisible = state.Settings.InboundMcpSettings?.Status.IsRunning == true;
            if (state.Settings.InboundMcpSettings?.Status is { IsRunning: true } inbound)
            {
                inboundMcpIndicator.Content = $"MCP · {inbound.ActiveClients.Count}";
                ToolTip.SetTip(inboundMcpIndicator,
                    $"Authenticated local control active\n{inbound.Endpoint}\nInstance {inbound.InstanceId}");
            }
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

        Button abort = new()
        {
            Content = "Abort",
            Classes = { "command" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(abort, $"Abort goal {goal.Title} and start new");
        abort.Click += async (_, _) => await AbortGoalAsync(goal);

        Grid heading = new() { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 10 };
        heading.Children.Add(new TextBlock
        {
            Text = goal.Title,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(select, 1);
        heading.Children.Add(select);
        Grid.SetColumn(abort, 2);
        heading.Children.Add(abort);
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
            ConversationWorkflowCardState.Paused => "attention",
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
            case ConversationWorkflowActionKind.RetryRun:
                await RetryRunAsync(goal);
                break;
            case ConversationWorkflowActionKind.AbortGoal:
                await AbortGoalAsync(goal);
                break;
            case ConversationWorkflowActionKind.ExtendBudget:
                await new BudgetExtensionDialog(store, goal, cancellationToken).ShowDialog(this);
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
            case ConversationWorkflowActionKind.ReviewBranchHandoff:
                await ReviewBranchHandoffAsync();
                break;
        }
    }

    private async Task StartPlanningAsync(GoalView goal)
    {
        if (store.Current.Settings.AgentDefaults is not { Models.Count: > 0 })
        {
            await store.DiscoverAgentDefaultsAsync(cancellationToken);
        }

        AgentDefaultsSnapshot? defaults = store.Current.Settings.AgentDefaults;
        GoalModelCandidate[] candidates = ModelSelectionCatalog.ForRole(
            defaults?.Models ?? [], AgentRole.Lead);
        GoalModelSelectionView? effective = store.Current.Goals.ModelSelections
            .FirstOrDefault(selection => selection.Role is AgentRole.Lead);
        AgentRoleDefault? configured = defaults?.Roles
            .FirstOrDefault(roleDefault => roleDefault.Role is AgentRole.Lead);
        GoalModelCandidate? preferred = candidates.FirstOrDefault(candidate =>
            candidate.Provider == effective?.Provider && candidate.Model == effective?.Model) ??
            candidates.FirstOrDefault(candidate =>
                candidate.Provider == configured?.Provider && candidate.Model == configured?.Model);
        PlanGenerationDialog dialog = new(
            candidates,
            preferred,
            GoalPresentationFormatter.StartDisclosure(store.Current.Goals));
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } result)
        {
            return;
        }

        if (result.LeadModel.Access is ModelAccess.Remote &&
            !await new RemoteModelAuthorizationDialog(
                    goal,
                    result.LeadModel,
                    AgentRole.Lead)
                .ShowDialog<bool>(this))
        {
            return;
        }

        await store.StartGoalWorkflowAsync(
            goal.Id,
            result.LeadModel,
            cancellationToken);
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
        await store.ResumeGoalWorkflowAsync(goal.Id, cancellationToken);
    }

    private async Task RetryRunAsync(GoalView goal)
    {
        if (store.Current.Goals.Workflow?.RetryRole is not { } retryRole)
        {
            return;
        }

        AgentRole role = retryRole switch
        {
            GoalWorkflowRetryRole.Lead => AgentRole.Lead,
            GoalWorkflowRetryRole.Implementer => AgentRole.Implementer,
            GoalWorkflowRetryRole.Reviewer => AgentRole.Reviewer,
            _ => throw new ArgumentOutOfRangeException(nameof(retryRole)),
        };
        if (store.Current.Settings.AgentDefaults is not { Models.Count: > 0 })
        {
            await store.DiscoverAgentDefaultsAsync(cancellationToken);
        }

        AgentDefaultsSnapshot? defaults = store.Current.Settings.AgentDefaults;
        GoalModelCandidate[] candidates = ModelSelectionCatalog.ForRole(
            defaults?.Models ?? [], role);
        GoalModelSelectionView? effective = store.Current.Goals.ModelSelections
            .FirstOrDefault(selection => selection.Role == role);
        AgentRoleDefault? configured = defaults?.Roles
            .FirstOrDefault(roleDefault => roleDefault.Role == role);
        GoalModelCandidate? preferred = candidates.FirstOrDefault(candidate =>
            candidate.Provider == effective?.Provider && candidate.Model == effective?.Model) ??
            candidates.FirstOrDefault(candidate =>
                candidate.Provider == configured?.Provider && candidate.Model == configured?.Model);
        WorkflowRetryDialog dialog = new(
            retryRole,
            candidates,
            preferred,
            GoalPresentationFormatter.RetryDisclosure(retryRole, store.Current.Goals));
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } result)
        {
            return;
        }

        if (result.Model.Access is ModelAccess.Remote &&
            !await new RemoteModelAuthorizationDialog(goal, result.Model, role)
                .ShowDialog<bool>(this))
        {
            return;
        }

        await store.RetryGoalWorkflowAsync(
            goal.Id,
            retryRole,
            result.Model,
            result.Guidance is null ? null : new(result.Guidance),
            cancellationToken);
    }

    private async Task AbortGoalAsync(GoalView goal)
    {
        AbortGoalDialog dialog = new(goal);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } reason)
        {
            return;
        }

        await store.AbortGoalAsync(goal.Id, reason, cancellationToken);
        if (store.Current.Goals.SelectedGoalId is null)
        {
            composer.Focus();
        }
    }

    private async Task ReviewBranchHandoffAsync()
    {
        if (workbench is null || !workbench.ShowGit())
        {
            return;
        }

        await workbench.RefreshGitAsync();
    }

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
