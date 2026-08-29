using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using AvaloniaEdit;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class GitHistoryTool
{
    private readonly WorkbenchToolContext context;
    private readonly Action<string> reportStatus;
    private readonly TextBox path = new() { PlaceholderText = "Optional repository path" };
    private readonly ListBox history = new();
    private readonly TextEditor details = CodeEditorView.Create(
        string.Empty, isReadOnly: true, wordWrap: false, showLineNumbers: false,
        path: "git-history.patch");
    private DeveloperGitHistoryPageView? currentPage;

    internal GitHistoryTool(WorkbenchToolContext context, Action<string> reportStatus)
    {
        this.context = context;
        this.reportStatus = reportStatus;
        Content = BuildContent();
    }

    internal Control Content { get; }

    internal async ValueTask RefreshAsync(bool append = false)
    {
        WorkspaceView? active = context.ActiveWorkspace();
        if (context.IsBusy() || active is null || context.DeveloperGitService is null) return;
        await context.RunAsync(() => RefreshCoreAsync(active, append));
    }

    internal async ValueTask RefreshCoreAsync(WorkspaceView active, bool append)
    {
        IDeveloperGitService service = context.DeveloperGitService!;
        string pathText = path.Text?.Trim() ?? string.Empty;
        DeveloperGitPath? selectedPath = pathText.Length == 0 ? null : new(pathText);
        DeveloperGitHistoryPageView? previous = currentPage;
        DeveloperGitHistoryCursor? cursor = append && previous is not null && previous.Path == selectedPath
            ? previous.NextCursor : null;
        DeveloperGitHistoryPageView page = await service.InspectHistoryAsync(new(
            context.Request(active), selectedPath, cursor, MaximumResults: 100), context.CancellationToken);
        if (append && previous is not null && page.Error is null)
            page = page with { Commits = previous.Commits.Concat(page.Commits).ToArray() };
        currentPage = page;
        history.ItemsSource = BuildChoices(page.Commits);
        if (!append) history.SelectedIndex = page.Commits.Count > 0 ? 0 : -1;
        reportStatus(page.Error ?? (page.Path is null
            ? $"Showing {page.Commits.Count} commits."
            : $"Showing {page.Commits.Count} commits for {page.Path.Value}."));
    }

    private async ValueTask ShowSelectedCommitAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null ||
            history.SelectedItem is not HistoryChoice selected) return;
        await context.RunAsync(async () =>
        {
            DeveloperGitCommitDetailResult result = await service.InspectCommitAsync(
                context.Request(active), selected.Commit.Sha, context.CancellationToken);
            if (result.Detail is null)
            {
                details.Text = result.Error ?? "The selected commit is unavailable.";
                return;
            }
            DeveloperGitCommitDetailView detail = result.Detail;
            string parents = detail.Parents.Count == 0 ? "root" :
                string.Join(", ", detail.Parents.Select(parent => parent.Value));
            string references = detail.References.Count == 0 ? "none" :
                string.Join(", ", detail.References);
            string diffs = string.Join("\n\n", detail.ParentDiffs.Select(diff =>
                $"--- {(diff.Parent is null ? "empty tree" : diff.Parent.Value)} -> {detail.Sha.Value} " +
                $"({diff.Paths.Count} path(s)){(diff.IsTruncated ? " · truncated" : string.Empty)} ---\n" +
                diff.Patch));
            details.Text = $"Commit {detail.Sha.Value}\nParents {parents}\nReferences {references}\n" +
                $"Author {detail.AuthorName} <{detail.AuthorEmail}> · {detail.AuthoredAt:u}\n" +
                $"Committer {detail.CommitterName} <{detail.CommitterEmail}> · {detail.CommittedAt:u}\n\n" +
                $"{detail.Message}{(detail.MessageIsTruncated ? "\n[message truncated]" : string.Empty)}\n\n{diffs}";
            reportStatus($"Showing exact parent/child diff for {detail.Sha.Value}.");
        });
    }

    private async ValueTask ShowBlameAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        string pathText = path.Text?.Trim() ?? string.Empty;
        if (context.IsBusy() || active is null || service is null || pathText.Length == 0)
        {
            reportStatus("Enter a repository path before opening blame.");
            return;
        }
        await context.RunAsync(async () =>
        {
            DeveloperGitBlamePageView page = await service.InspectBlameAsync(new(
                context.Request(active), new(pathText), StartLine: 1, MaximumLines: 500),
                context.CancellationToken);
            details.Text = page.Error ?? string.Join('\n', page.Lines.Select(line =>
                $"{line.LineNumber,6} {line.Commit.Value[..Math.Min(8, line.Commit.Value.Length)]} " +
                $"{line.AuthorName} {line.OriginalPath.Value}:{line.OriginalLineNumber}  {line.Text}")) +
                (page.NextStartLine is null ? string.Empty :
                    $"\n\nBlame is paged; next line is {page.NextStartLine.Value}.");
            reportStatus(page.Error ?? $"Showing blame for {pathText}.");
        });
    }

    private Control BuildContent()
    {
        WrapPanel actions = new() { Orientation = Orientation.Horizontal };
        Button refresh = new() { Content = "Refresh history" };
        Button more = new() { Content = "Load more" };
        Button blame = new() { Content = "Blame path" };
        foreach (Button button in new[] { refresh, more, blame })
            button.Margin = new Thickness(0, 0, 6, 6);
        AutomationProperties.SetName(path, "Optional path for Git file history and blame");
        AutomationProperties.SetName(refresh, "Refresh Git history or file timeline");
        AutomationProperties.SetName(more, "Load next page of Git history");
        AutomationProperties.SetName(blame, "Show blame for repository path");
        AutomationProperties.SetName(history, "Paged Git commit history");
        AutomationProperties.SetName(details, "Selected Git commit details and parent diffs");
        refresh.Click += async (_, _) => await RefreshAsync();
        more.Click += async (_, _) => await RefreshAsync(append: true);
        blame.Click += async (_, _) => await ShowBlameAsync();
        history.SelectionChanged += async (_, _) => await ShowSelectedCommitAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(more);
        actions.Children.Add(blame);
        Grid panel = new() { RowDefinitions = new("Auto,Auto,*,2*"), RowSpacing = 8 };
        panel.Children.Add(path);
        Grid.SetRow(actions, 1);
        panel.Children.Add(actions);
        Grid.SetRow(history, 2);
        panel.Children.Add(history);
        Grid.SetRow(details, 3);
        panel.Children.Add(details);
        return panel;
    }

    private static IReadOnlyList<HistoryChoice> BuildChoices(
        IReadOnlyList<DeveloperGitHistoryCommitView> commits)
    {
        var lanes = new List<string>();
        var choices = new List<HistoryChoice>(commits.Count);
        foreach (DeveloperGitHistoryCommitView commit in commits)
        {
            int lane = lanes.IndexOf(commit.Sha.Value);
            if (lane < 0)
            {
                lane = lanes.Count;
                lanes.Add(commit.Sha.Value);
            }
            string graph = string.Join(' ', Enumerable.Range(0, lanes.Count)
                .Select(index => index == lane ? "●" : "│"));
            lanes.RemoveAt(lane);
            for (int parent = commit.Parents.Count - 1; parent >= 0; parent--)
                if (!lanes.Contains(commit.Parents[parent].Value, StringComparer.Ordinal))
                    lanes.Insert(Math.Min(lane, lanes.Count), commit.Parents[parent].Value);
            choices.Add(new(graph, commit));
        }
        return choices;
    }

    private sealed record HistoryChoice(string Graph, DeveloperGitHistoryCommitView Commit)
    {
        public override string ToString()
        {
            string sha = Commit.Sha.Value[..Math.Min(8, Commit.Sha.Value.Length)];
            string references = Commit.References.Count == 0 ? string.Empty :
                $" · {string.Join(", ", Commit.References)}";
            string merge = Commit.Parents.Count > 1 ? " · merge" : string.Empty;
            return $"{Graph} {sha} · {Commit.Subject} · {Commit.AuthorName} · " +
                   $"{Commit.AuthoredAt.LocalDateTime:g}{references}{merge}";
        }
    }
}
