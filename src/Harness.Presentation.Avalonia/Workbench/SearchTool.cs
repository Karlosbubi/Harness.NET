using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class SearchTool
{
    private readonly WorkbenchToolContext context;
    private readonly Action<string> reportStatus;
    private readonly TextBox query = new();
    private readonly ListBox results = new();

    internal SearchTool(WorkbenchToolContext context, Action<string> reportStatus)
    {
        this.context = context;
        this.reportStatus = reportStatus;
        Content = BuildContent();
    }

    internal Control Content { get; }
    internal TextBox Query => query;
    internal ListBox Results => results;

    internal void Reset()
    {
        results.ItemsSource = Array.Empty<SearchChoice>();
        results.IsVisible = false;
    }

    private Control BuildContent()
    {
        Grid searchRow = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        query.PlaceholderText = "Search file contents";
        query.Classes.Add("workspace-input");
        AutomationProperties.SetName(query, "Search tracked workspace text");
        AccessibleIconButton search = new()
        {
            Content = "⌕",
            AccessibleName = "Run tracked workspace search",
        };
        search.Classes.Add("icon");
        AutomationProperties.SetName(search, "Run tracked workspace search");
        search.Click += async (_, _) => await SearchAsync();
        query.KeyDown += async (_, args) =>
        {
            if (args.Key is Key.Enter)
            {
                args.Handled = true;
                await SearchAsync();
            }
        };
        searchRow.Children.Add(query);
        Grid.SetColumn(search, 1);
        searchRow.Children.Add(search);

        AutomationProperties.SetName(results, "Tracked-text search results");
        results.MaxHeight = 180;
        results.IsVisible = false;
        results.DoubleTapped += async (_, _) =>
        {
            if (results.SelectedItem is SearchChoice choice)
            {
                await context.OpenFileAsync(choice.Match.Path, choice.GoalId);
            }
        };
        Grid.SetRow(results, 1);

        return new Grid
        {
            RowDefinitions = new("Auto,Auto"),
            RowSpacing = 6,
            Children =
            {
                searchRow,
                results,
            },
        };
    }

    internal async ValueTask SearchAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        if (context.IsBusy() || active is null || !active.IsTrusted ||
            string.IsNullOrWhiteSpace(query.Text))
        {
            reportStatus(active is null
                ? "Select a workspace first."
                : active.IsTrusted
                    ? "Enter text to search."
                    : "Trust the workspace before searching files.");
            return;
        }

        await context.RunAsync(async () =>
        {
            WorkbenchTextSearchResult inspected = await context.InspectionService.SearchTextAsync(
                context.Request(active),
                query.Text.Trim(),
                context.CancellationToken);
            WorkspaceTextSearchView result = inspected.Search;
            results.ItemsSource = result.Matches
                .Select(match => new SearchChoice(match, inspected.Context.GoalId))
                .ToArray();
            results.IsVisible = result.Matches.Count > 0;
            reportStatus(result.Error ??
                         $"{inspected.Context.Description} · {result.Matches.Count} match(es) " +
                         $"in {result.FilesScanned} file(s)" +
                         (result.IsTruncated ? " · truncated." : "."));
        });
    }

    private sealed record SearchChoice(WorkspaceTextMatchView Match, GoalId? GoalId)
    {
        public override string ToString() => $"{Match.Path}:{Match.LineNumber}  {Match.Text}";
    }
}
