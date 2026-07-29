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
    private readonly TextBlock providerDetails = new();
    private readonly TextBlock themeIssues = new();
    private readonly TextBlock goalContext = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ItemsControl evidence = new();
    private readonly ComboBox modelPicker = new();
    private readonly ComboBox themePicker = new();
    private readonly Button send = new() { Content = "Send" };
    private readonly Button cancel = new() { Content = "Cancel" };
    private readonly Button manageWorkspaces = new() { Content = "Manage workspaces" };
    private readonly Button manageFramework = new() { Content = "Engineering framework" };
    private readonly Button manageGoals = new() { Content = "Goals and plans" };
    private readonly Button operations = new() { Content = "Application operations" };
    private readonly AccessibleIconButton refreshProvider = new()
    {
        Content = "Refresh",
        AccessibleName = "Refresh provider models",
    };
    private readonly AccessibleIconButton reloadThemes = new()
    {
        Content = "Reload",
        AccessibleName = "Reload user themes",
    };
    private readonly Border header = new();
    private readonly Border navigation = new();
    private readonly Border primary = new();
    private readonly Border utility = new();
    private readonly Border footer = new();
    private readonly TextBlock brandDetail = new()
    {
        Text = "Local-first agent workspace",
        FontSize = 11,
    };
    private readonly TextBlock modelLabel = new()
    {
        Text = "Model",
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
            Dispatcher.UIThread.Post(ApplyTheme)));
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
            cancellationToken);
        Grid.SetRow(workbench.Control, 1);
        root.Children.Add(workbench.Control);

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
            ColumnDefinitions = new("Auto,*"),
            Margin = new(14, 8),
            ColumnSpacing = 16,
        };
        StackPanel title = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Harness.NET", FontSize = 17, FontWeight = FontWeight.SemiBold },
                brandDetail,
            },
        };
        grid.Children.Add(title);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                modelLabel,
                modelPicker,
                refreshProvider,
                themeLabel,
                themePicker,
                reloadThemes,
                workbench?.LayoutActions ?? new TextBlock(),
            },
        };
        ScrollViewer actionScroller = new()
        {
            Content = actions,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(actionScroller, "Workbench commands");
        Grid.SetColumn(actionScroller, 1);
        grid.Children.Add(actionScroller);
        AutomationProperties.SetName(modelPicker, "Conversation model");
        AutomationProperties.SetName(themePicker, "Color theme");
        modelPicker.MinWidth = 120;
        themePicker.MinWidth = 100;
        return grid;
    }

    private void UpdateResponsiveChrome(double width)
    {
        bool compact = width > 0 && width < 1024;
        brandDetail.IsVisible = !compact;
        modelLabel.IsVisible = !compact;
        themeLabel.IsVisible = !compact;
    }

    private Control BuildNavigation()
    {
        StackPanel panel = new()
        {
            Margin = new(14),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "WORKSPACE", FontSize = 11, FontWeight = FontWeight.Bold },
                workspace,
                manageWorkspaces,
                new Separator(),
                new TextBlock { Text = "AVAILABLE", FontSize = 11, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "● Conversation", TextWrapping = TextWrapping.Wrap },
                manageFramework,
                manageGoals,
                operations,
            },
        };
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
        AutomationProperties.SetName(send, "Send message");
        composerArea.Children.Add(send);
        Grid.SetColumn(cancel, 2);
        cancel.VerticalAlignment = VerticalAlignment.Bottom;
        AutomationProperties.SetName(cancel, "Cancel current response");
        composerArea.Children.Add(cancel);
        Grid.SetRow(composerArea, 2);
        grid.Children.Add(composerArea);
        return grid;
    }

    private Control BuildUtility()
    {
        StackPanel panel = new()
        {
            Margin = new(14),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "PROVIDER", FontSize = 11, FontWeight = FontWeight.Bold },
                providerDetails,
                new Separator(),
                new TextBlock { Text = "APPEARANCE", FontSize = 11, FontWeight = FontWeight.Bold },
                themeIssues,
                new Separator(),
                new TextBlock { Text = "GOAL CONTEXT", FontSize = 11, FontWeight = FontWeight.Bold },
                goalContext,
                evidence,
            },
        };
        AutomationProperties.SetName(panel, "Provider, context, and evidence details");
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
        manageWorkspaces.Click += async (_, _) =>
        {
            WorkspaceDialog dialog = new(store, cancellationToken);
            await dialog.ShowDialog(this);
        };
        manageGoals.Click += async (_, _) =>
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
        composer.Focus();
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
            DashboardSnapshot? dashboard = state.Dashboard;
            if (dashboard is not null)
            {
                bool hasWorkspace = state.Workspaces.Registered.Any(item => item.IsActive);
                workspace.Text = hasWorkspace
                    ? $"{dashboard.Workspace.Name}\n{dashboard.Workspace.Branch}\n{dashboard.Workspace.Trust}"
                    : "No workspace selected\nRegister a repository to enable goals, " +
                      "framework rules, and typed tools.";
                activities.ItemsSource = dashboard.Activities
                    .Select(CreateMessageCard)
                    .ToArray();
                providerDetails.Text = ProviderText(dashboard.Provider);
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
                Dispatcher.UIThread.Post(conversationScroll.ScrollToEnd);
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
                themeIssues.Text = appearance.Issues.Count == 0
                    ? "All themes valid"
                    : string.Join("\n", appearance.Issues.Select(issue =>
                        $"⚠ {issue.SourceName}: {issue.Message}"));
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
        navigation.Background = Brush(UiThemeColorToken.Panel);
        primary.Background = Brush(UiThemeColorToken.Editor);
        utility.Background = Brush(UiThemeColorToken.Panel);
        footer.Background = Brush(UiThemeColorToken.Header);
        Background = Brush(UiThemeColorToken.Window);
        Foreground = Brush(UiThemeColorToken.TextPrimary);
        status.RefreshTheme();
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
        return new Border
        {
            Child = body,
            Padding = new Thickness(13, 10),
            Margin = isUser ? new Thickness(52, 0, 4, 10) : new Thickness(4, 0, 52, 10),
            CornerRadius = new CornerRadius(9),
            Background = Brush(isUser ? UiThemeColorToken.AccentSoft : UiThemeColorToken.Panel),
            BorderBrush = Brush(UiThemeColorToken.Border),
            BorderThickness = new Thickness(1),
        };
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
