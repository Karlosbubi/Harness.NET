using System.Globalization;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed class SemanticContextDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly GoalView goal;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly TextBox profile = Viewer();
    private readonly TextBox rebuildResult = Viewer();
    private readonly TextBox searchResult = Viewer();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button rebuild = new() { Content = "Rebuild index…" };
    private readonly Button search = new() { Content = "Preview search…" };
    private readonly Button cancel = new() { Content = "Cancel operation" };

    internal SemanticContextDialog(
        AvaloniaPresentationStore store,
        GoalView goal,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.goal = goal;
        this.cancellationToken = cancellationToken;
        Title = "Semantic context";
        Width = 940;
        Height = 700;
        MinWidth = 720;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Goals)));
        Closed += (_, _) =>
        {
            if (store.Current.Goals.IsSemanticRunning)
            {
                store.CancelSemanticOperation();
            }

            subscription.Dispose();
        };
    }

    private Control BuildContent()
    {
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        TabControl tabs = new()
        {
            ItemsSource = new TabItem[]
            {
                new() { Header = "Status & route", Content = profile },
                new() { Header = "Last rebuild", Content = rebuildResult },
                new() { Header = "Search matches", Content = searchResult },
            },
        };
        Grid root = new()
        {
            RowDefinitions = new("Auto,*,Auto,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(20),
        };
        root.Children.Add(new TextBlock
        {
            Text = "Goal-attributed semantic context",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { rebuild, search, cancel },
        };
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        Grid footer = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(status);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return root;
    }

    private void WireInteractions()
    {
        rebuild.Click += async (_, _) =>
        {
            SemanticIndexStatusResult? semanticStatus = store.Current.Goals.SemanticStatus;
            if (semanticStatus is null)
            {
                return;
            }

            SemanticRebuildConfirmationDialog confirmation = new(
                goal,
                semanticStatus,
                store.Current.Goals.Cost);
            if (await confirmation.ShowDialog<bool>(this))
            {
                await store.RebuildSemanticIndexAsync(goal.Id, cancellationToken);
            }
        };
        search.Click += async (_, _) =>
        {
            TextEntryDialog query = new(
                "Preview semantic context",
                "Query (one attributed embedding call, maximum 2,000 characters)",
                "Search up to 8 matches",
                "A query is required.");
            await query.ShowDialog(this);
            if (query.Result is not null)
            {
                await store.SearchSemanticContextAsync(goal.Id, query.Result, cancellationToken);
            }
        };
        cancel.Click += (_, _) => store.CancelSemanticOperation();
    }

    private void Render(GoalManagementState state)
    {
        profile.Text = GoalPresentationFormatter.FormatSemanticStatus(
            state.SemanticStatus,
            goal,
            state.Cost);
        rebuildResult.Text = GoalPresentationFormatter.FormatSemanticRebuild(state.SemanticRebuild);
        searchResult.Text = GoalPresentationFormatter.FormatSemanticSearch(state.SemanticSearch);
        bool busy = state.IsSemanticRunning;
        SemanticIndexStatusResult? semanticStatus = state.SemanticStatus;
        rebuild.IsEnabled = !state.IsBusy && semanticStatus is { Error: null };
        search.IsEnabled = !state.IsBusy &&
                           semanticStatus is { Error: null, CurrentPartition: not null };
        cancel.IsVisible = busy;
        cancel.IsEnabled = busy;
        status.Text = busy ? "Embedding operation running…" : state.Status ?? string.Empty;
    }

    private static TextBox Viewer() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
    };
}

