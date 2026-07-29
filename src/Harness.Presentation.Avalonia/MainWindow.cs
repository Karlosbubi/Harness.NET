using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed class MainWindow : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly HarnessThemeController themeController;
    private readonly IRunOutputService runOutputService;
    private readonly IWorkbenchInspectionService inspectionService;
    private readonly IWorkbenchDocumentService documentService;
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
    private readonly ComboBox themePicker = new();
    private readonly Button send = new() { Content = "Send" };
    private readonly Button cancel = new() { Content = "Cancel" };
    private readonly Button openWorkspace = new() { Content = "Open workspace" };
    private readonly Button manageWorkspaces = new() { Content = "Workspaces…" };
    private readonly Button manageFramework = new() { Content = "Framework" };
    private readonly Button manageGoals = new() { Content = "Goals" };
    private readonly Button goalAction = new() { Content = "Create or select a goal" };
    private readonly Button operations = new() { Content = "Operations" };
    private readonly AccessibleIconButton refreshProvider = new()
    {
        Content = "↻",
        AccessibleName = "Refresh provider models",
    };
    private readonly AccessibleIconButton reloadThemes = new()
    {
        Content = "↻",
        AccessibleName = "Reload user themes",
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
    private readonly TextBlock themeLabel = new()
    {
        Text = "Theme",
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
        IWorkbenchLayoutService layoutService,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.themeController = themeController;
        this.runOutputService = runOutputService;
        this.inspectionService = inspectionService;
        this.documentService = documentService;
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
                RenderActivities(store.Current.Dashboard);
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
            layoutService,
            new AvaloniaWorkbenchDocumentPrompt(),
            () => store.Current,
            navigation,
            primary,
            utility,
            cancellationToken,
            ShowWorkspaceDialogAsync);
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
            ColumnDefinitions = new("Auto,Auto,*"),
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
                Cluster(themeLabel, themePicker, reloadThemes),
                workbench?.LayoutActions is { } layoutActions
                    ? Cluster(layoutActions)
                    : new TextBlock(),
            },
        };
        ScrollViewer actionScroller = new()
        {
            Content = actions,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(actionScroller, "Workbench commands");
        Grid.SetColumn(actionScroller, 2);
        grid.Children.Add(actionScroller);
        AutomationProperties.SetName(modelPicker, "Conversation model");
        AutomationProperties.SetName(themePicker, "Color theme");
        modelPicker.MinWidth = 120;
        themePicker.MinWidth = 100;
        modelPicker.Classes.Add("toolbar-input");
        themePicker.Classes.Add("toolbar-input");
        refreshProvider.Classes.Add("icon");
        reloadThemes.Classes.Add("icon");
        modelLabel.Classes.Add("cluster-label");
        themeLabel.Classes.Add("cluster-label");
        return grid;
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
        modelLabel.IsVisible = !compact;
        themeLabel.IsVisible = !compact;
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
                manageGoals,
                manageFramework,
                new Separator(),
                new TextBlock { Text = "APPLICATION", FontSize = 11, FontWeight = FontWeight.Bold },
                operations,
            },
        };
        foreach (Button button in new[] { manageWorkspaces, manageGoals, manageFramework, operations })
        {
            button.Classes.Add("command");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
        }
        AutomationProperties.SetName(manageWorkspaces, "Manage workspaces");
        AutomationProperties.SetName(manageGoals, "Goals and plans");
        AutomationProperties.SetName(manageFramework, "Engineering framework");
        AutomationProperties.SetName(operations, "Application operations");
        AutomationProperties.SetName(panel, "Workspace navigation");
        return panel;
    }

    private Control BuildPrimary()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*,Auto"),
            Margin = new(1, 0),
        };
        TextBlock heading = new()
        {
            Text = "Durable conversation",
            Margin = new(16, 12),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        };
        grid.Children.Add(heading);

        activities.Margin = new(4, 0);
        AutomationProperties.SetName(activities, "Conversation activity");
        conversationScroll.Content = activities;
        conversationScroll.Margin = new(12, 0);
        Grid.SetRow(conversationScroll, 1);
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
        AutomationProperties.SetName(composer, "Message the local model");
        composerArea.Children.Add(composer);
        Grid.SetColumn(send, 1);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        send.Classes.Add("primary");
        AutomationProperties.SetName(send, "Send message");
        composerArea.Children.Add(send);
        Grid.SetColumn(cancel, 2);
        cancel.VerticalAlignment = VerticalAlignment.Bottom;
        cancel.Classes.Add("command");
        AutomationProperties.SetName(cancel, "Cancel current response");
        composerArea.Children.Add(cancel);
        Grid.SetRow(composerArea, 2);
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
                goalAction,
                new Separator(),
                new TextBlock { Text = "RECENT EVIDENCE", Classes = { "eyebrow" } },
                evidence,
            },
        };
        goalAction.Classes.Add("command");
        AutomationProperties.SetName(goalAction, "Create or select a goal");
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
                await store.SubmitAsync(cancellationToken);
            }
            else if (eventArgs.Key is Key.Escape && store.Current.IsStreaming)
            {
                store.CancelSubmission();
            }
        };
        send.Click += async (_, _) => await store.SubmitAsync(cancellationToken);
        cancel.Click += (_, _) => store.CancelSubmission();
        modelPicker.SelectionChanged += async (_, _) =>
        {
            if (!suppressSelection && modelPicker.SelectedItem is string model)
            {
                await store.SelectModelAsync(model, cancellationToken);
            }
        };
        themePicker.SelectionChanged += async (_, _) =>
        {
            if (!suppressSelection && themePicker.SelectedItem is ThemeChoice choice)
            {
                await store.SelectThemeAsync(choice.Id, cancellationToken);
            }
        };
        refreshProvider.Click +=
            async (_, _) => await store.RefreshProviderAsync(cancellationToken);
        reloadThemes.Click +=
            async (_, _) => await store.RefreshThemesAsync(cancellationToken);
        openWorkspace.Click += async (_, _) => await ShowWorkspaceDialogAsync(true);
        manageWorkspaces.Click += async (_, _) => await ShowWorkspaceDialogAsync(false);
        manageGoals.Click += async (_, _) =>
        {
            GoalDialog dialog = new(store, cancellationToken);
            await dialog.ShowDialog(this);
        };
        goalAction.Click += async (_, _) =>
        {
            GoalDialog dialog = new(store, cancellationToken);
            await dialog.ShowDialog(this);
        };
        manageFramework.Click += async (_, _) =>
        {
            FrameworkDialog dialog = new(store, cancellationToken);
            await dialog.ShowDialog(this);
        };
        operations.Click += async (_, _) =>
        {
            OperationsDialog dialog = new(store, cancellationToken);
            await dialog.ShowDialog(this);
        };
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
            browseOnOpen: browseImmediately);
        await dialog.ShowDialog(this);
    }

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
            send.IsEnabled = !state.IsLoading && !state.IsStreaming &&
                             !string.IsNullOrWhiteSpace(state.ComposerText);
            cancel.IsVisible = state.IsStreaming;
            manageGoals.IsEnabled = !state.IsLoading &&
                                    state.Workspaces.Registered.Any(item => item.IsActive);
            manageFramework.IsEnabled = !state.IsLoading &&
                                        state.Workspaces.Registered.Any(item => item.IsActive);
            goalAction.IsEnabled = manageGoals.IsEnabled;
            goalAction.Content = manageGoals.IsEnabled
                ? "Create or select a goal"
                : "Open a workspace first";
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
                RenderActivities(dashboard);
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
                ThemeChoice[] choices = appearance.Themes
                    .Select(theme => new ThemeChoice(theme.Id.Value, theme.DisplayName))
                    .ToArray();
                themePicker.ItemsSource = choices;
                themePicker.SelectedItem = choices.FirstOrDefault(choice =>
                    choice.Id == appearance.PreferredThemeId.Value);
                ToolTip.SetTip(
                    themePicker,
                    appearance.Issues.Count == 0
                        ? "All installed themes are valid."
                        : string.Join("\n", appearance.Issues.Select(issue =>
                            $"⚠ {issue.SourceName}: {issue.Message}")));
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

    private void RenderActivities(DashboardSnapshot? dashboard)
    {
        activities.ItemsSource = dashboard?.Activities
            .Select(CreateMessageCard)
            .ToArray() ?? [];
        Dispatcher.UIThread.Post(conversationScroll.ScrollToEnd);
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

    private sealed record ThemeChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

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
