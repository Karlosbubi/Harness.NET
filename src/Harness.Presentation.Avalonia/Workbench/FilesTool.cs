using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class FilesTool
{
    private readonly WorkbenchToolContext context;
    private readonly TextBox filter = new();
    private readonly TreeView tree = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly SearchTool search;
    private bool busy;
    private int contextVersion;
    private string? workspaceId;
    private string? selectedGoalId;
    private IReadOnlyList<WorkbenchDocumentPath> trackedFiles = [];
    private string? error;
    private string? sourceContext;
    private bool truncated;

    internal FilesTool(WorkbenchToolContext context)
    {
        this.context = context;
        search = new(context, ReportStatus);
        Content = BuildContent();
    }

    internal Control Content { get; }
    internal TreeView Tree => tree;
    internal TextBox Filter => filter;
    internal string StatusText => status.Text ?? string.Empty;

    internal void Update(AvaloniaShellState snapshot)
    {
        WorkspaceView? active = snapshot.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        if (!string.Equals(workspaceId, active?.Id, StringComparison.Ordinal))
        {
            contextVersion++;
            workspaceId = active?.Id;
            search.Reset();
            trackedFiles = [];
            error = null;
            sourceContext = null;
            truncated = false;
            RenderTree();
            ReportStatus(string.Empty);
            if (active is { IsTrusted: true })
            {
                Dispatcher.UIThread.Post(async () => await RefreshAsync());
            }
        }

        string? nextGoalId = snapshot.Goals.SelectedGoal is { } selectedGoal && active is not null &&
                             selectedGoal.WorkspaceId == active.Id
            ? selectedGoal.Id.Value
            : null;
        if (!string.Equals(selectedGoalId, nextGoalId, StringComparison.Ordinal))
        {
            contextVersion++;
            selectedGoalId = nextGoalId;
            search.Reset();
            ReportStatus(nextGoalId is null
                ? "Source context: original workspace."
                : "Source context changed; refreshing the selected goal worktree.");
            if (active is { IsTrusted: true })
            {
                Dispatcher.UIThread.Post(async () => await RefreshAsync());
            }
        }
    }

    internal async ValueTask RefreshAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        if (busy)
        {
            return;
        }

        if (active is null || !active.IsTrusted)
        {
            trackedFiles = [];
            error = active is null
                ? "Select a workspace to browse its files."
                : "Trust the workspace to browse its files.";
            truncated = false;
            sourceContext = null;
            RenderTree();
            return;
        }

        int refreshVersion = contextVersion;
        busy = true;
        error = null;
        ReportStatus("Loading repository files…");
        try
        {
            WorkbenchFileCatalogResult result = await context.InspectionService.ListFilesAsync(
                context.Request(active),
                context.CancellationToken);
            if (refreshVersion != contextVersion)
            {
                return;
            }

            trackedFiles = result.Catalog.Files;
            truncated = result.Catalog.IsTruncated;
            error = result.Catalog.Error;
            sourceContext = result.Context.Description;
            RenderTree();
        }
        catch (OperationCanceledException)
        {
            if (refreshVersion == contextVersion)
            {
                error = "Repository file loading was cancelled.";
                RenderTree();
            }
        }
        catch (Exception exception)
        {
            if (refreshVersion == contextVersion)
            {
                error = exception.Message;
                RenderTree();
            }
        }
        finally
        {
            busy = false;
            if (refreshVersion != contextVersion)
            {
                Dispatcher.UIThread.Post(async () => await RefreshAsync());
            }
        }
    }

    internal async ValueTask<IReadOnlyList<PaletteCommand>> BuildFileCommandsAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        if (active is null || !active.IsTrusted)
        {
            return [];
        }

        if (trackedFiles.Count == 0 && error is null)
        {
            await RefreshAsync();
        }

        return
        [
            .. trackedFiles.Select(path => new PaletteCommand(
                $"file:{path.Value}",
                Directory(path.Value),
                Name(path.Value),
                () => context.OpenFileAsync(path.Value, context.SelectedGoalId()),
                MatchText: path.Value))
        ];
    }

    internal void ReportStatus(string message) => status.Text = message;

    private Control BuildContent()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*,Auto,Auto"),
            Margin = new Thickness(8),
            RowSpacing = 6,
        };
        Grid filterRow = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
        filter.PlaceholderText = "Filter repository files";
        filter.Classes.Add("workspace-input");
        AutomationProperties.SetName(filter, "Filter repository file tree");
        filter.TextChanged += (_, _) => RenderTree();
        AccessibleIconButton refresh = new()
        {
            Content = "↻",
            AccessibleName = "Refresh repository file tree",
        };
        refresh.Classes.Add("icon");
        refresh.Click += async (_, _) => await RefreshAsync();
        filterRow.Children.Add(filter);
        Grid.SetColumn(refresh, 1);
        filterRow.Children.Add(refresh);
        grid.Children.Add(filterRow);

        tree.ItemTemplate = new FuncTreeDataTemplate<FileTreeNode>(
            (node, _) =>
            {
                if (node.Path is { } filePath)
                {
                    Button file = new()
                    {
                        Content = node.Name,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                    };
                    file.Classes.Add("tree-file");
                    AutomationProperties.SetName(file, filePath.Value);
                    file.Click += async (_, _) =>
                        await context.OpenFileAsync(filePath.Value, context.SelectedGoalId());
                    return file;
                }

                return new TextBlock
                {
                    Text = node.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            },
            node => node.Children);
        AutomationProperties.SetName(tree, "Repository file tree");
        Grid.SetRow(tree, 1);
        grid.Children.Add(tree);

        Grid.SetRow(search.Content, 2);
        grid.Children.Add(search.Content);
        status.Classes.Add("muted");
        Grid.SetRow(status, 3);
        grid.Children.Add(status);
        return grid;
    }

    private void RenderTree()
    {
        string filterText = filter.Text?.Trim() ?? string.Empty;
        IReadOnlyList<WorkbenchDocumentPath> visible = filterText.Length == 0
            ? trackedFiles
            : trackedFiles
                .Where(file => file.Value.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        tree.ItemsSource = BuildFileTree(visible);
        status.Text = error ?? (trackedFiles.Count == 0
            ? "No tracked files are available in the current source context."
            : filterText.Length == 0
                ? $"{trackedFiles.Count:N0} tracked files" +
                  (truncated ? " · list truncated" : string.Empty) +
                  (string.IsNullOrWhiteSpace(sourceContext) ? string.Empty : $" · {sourceContext}")
                : $"{visible.Count:N0} of {trackedFiles.Count:N0} tracked files");
    }

    private static IReadOnlyList<FileTreeNode> BuildFileTree(
        IReadOnlyList<WorkbenchDocumentPath> paths)
    {
        FileTreeBuilder root = new(string.Empty);
        foreach (WorkbenchDocumentPath path in paths)
        {
            string[] segments = path.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            FileTreeBuilder parent = root;
            foreach (string directory in segments[..^1])
            {
                parent = parent.Directory(directory);
            }

            parent.Files.Add(new(segments[^1], path, []));
        }

        return root.ToNodes();
    }

    private static string Directory(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator <= 0 ? "(repository root)" : path[..separator];
    }

    private static string Name(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    internal sealed record FileTreeNode(
        string Name,
        WorkbenchDocumentPath? Path,
        IReadOnlyList<FileTreeNode> Children);

    private sealed class FileTreeBuilder(string name)
    {
        private readonly Dictionary<string, FileTreeBuilder> directories =
            new(StringComparer.Ordinal);

        internal List<FileTreeNode> Files { get; } = [];
        internal string Name { get; } = name;

        internal FileTreeBuilder Directory(string directory)
        {
            if (!directories.TryGetValue(directory, out FileTreeBuilder? child))
            {
                child = new(directory);
                directories.Add(directory, child);
            }

            return child;
        }

        internal IReadOnlyList<FileTreeNode> ToNodes() =>
            directories.Values
                .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                .Select(directory => new FileTreeNode(
                    directory.Name,
                    Path: null,
                    directory.ToNodes()))
                .Concat(Files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
    }
}
