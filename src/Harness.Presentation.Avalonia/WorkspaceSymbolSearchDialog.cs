using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Harness.BusinessLogic.CodeIntelligence;

namespace Harness.Presentation.Avalonia;

internal sealed class WorkspaceSymbolSearchDialog : Window
{
    private readonly Func<string, CancellationToken,
        ValueTask<WorkbenchCodeSemanticView>> search;
    private readonly Func<WorkbenchCodeSymbolDestination, ValueTask> navigate;
    private readonly TextBox query = new() { PlaceholderText = "Type or member name" };
    private readonly ListBox results = new() { MinHeight = 300 };
    private readonly TextBlock status = new() { Text = "Enter a symbol name." };
    private readonly Button open = new() { Content = "Open", IsEnabled = false };
    private CancellationTokenSource? searchCancellation;

    internal WorkspaceSymbolSearchDialog(
        Func<string, CancellationToken, ValueTask<WorkbenchCodeSemanticView>> search,
        Func<WorkbenchCodeSymbolDestination, ValueTask> navigate)
    {
        this.search = search;
        this.navigate = navigate;
        Title = "Workspace symbols";
        Width = 680;
        Height = 560;
        MinWidth = 440;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        AutomationProperties.SetName(query, "Workspace symbol query");
        AutomationProperties.SetName(results, "Workspace symbol results");
        AutomationProperties.SetName(status, "Workspace symbol search status");
        query.TextChanged += (_, _) => QueueSearch();
        query.KeyDown += async (_, args) =>
        {
            if (args.Key is Key.Enter && results.SelectedItem is SymbolChoice)
            {
                args.Handled = true;
                await OpenSelectedAsync();
            }
        };
        results.SelectionChanged += (_, _) =>
            open.IsEnabled = results.SelectedItem is SymbolChoice;
        results.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        open.Click += async (_, _) => await OpenSelectedAsync();
        Closed += (_, _) =>
        {
            searchCancellation?.Cancel();
            searchCancellation?.Dispose();
        };
        Opened += (_, _) => _ = query.Focus();
    }

    private Control BuildContent()
    {
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        open.Classes.Add("primary");
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { close, open },
        };
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*,Auto,Auto"),
            RowSpacing = 10,
            Margin = new global::Avalonia.Thickness(18),
            Children = { query },
        };
        Grid.SetRow(results, 1);
        grid.Children.Add(results);
        Grid.SetRow(status, 2);
        grid.Children.Add(status);
        Grid.SetRow(actions, 3);
        grid.Children.Add(actions);
        return grid;
    }

    private void QueueSearch()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new();
        _ = SearchAsync(query.Text?.Trim() ?? string.Empty, searchCancellation.Token);
    }

    private async Task SearchAsync(string value, CancellationToken cancellationToken)
    {
        results.ItemsSource = null;
        open.IsEnabled = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            status.Text = "Enter a symbol name.";
            return;
        }

        try
        {
            status.Text = "Searching Roslyn workspace…";
            await Task.Delay(TimeSpan.FromMilliseconds(140), cancellationToken);
            WorkbenchCodeSemanticView result = await search(value, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SymbolChoice[] choices = result.Items
                .Where(item => item.Relation is WorkbenchCodeSemanticRelation.Symbol)
                .Select(item => new SymbolChoice(item))
                .ToArray();
            results.ItemsSource = choices;
            if (choices.Length > 0)
                results.SelectedIndex = 0;
            status.Text = result.State is WorkbenchCodeResultState.Ready or
                WorkbenchCodeResultState.Degraded
                ? $"{choices.Length:N0} symbol(s)" +
                  (result.IsTruncated ? " · refine the query for more." : ".")
                : result.Issues.FirstOrDefault()?.Message.Value ?? "Symbol search failed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask OpenSelectedAsync()
    {
        if (results.SelectedItem is not SymbolChoice choice)
            return;
        Close();
        await navigate(choice.Item.Destination);
    }

    private sealed record SymbolChoice(WorkbenchCodeSemanticItem Item)
    {
        public override string ToString() => Item.Display.Value;
    }
}
