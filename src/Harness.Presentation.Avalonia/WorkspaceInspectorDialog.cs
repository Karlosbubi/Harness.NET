using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed class WorkspaceInspectorDialog : Window
{
    private readonly IWorkspaceInspectionService inspectionService;
    private readonly WorkspaceView workspace;
    private readonly CancellationToken cancellationToken;
    private readonly TextBox path = new();
    private readonly TextBox query = new();
    private readonly ListBox results = new();
    private readonly ListBox changes = new();
    private readonly ListBox projects = new();
    private readonly TextEditor source = CodeEditorView.Create();
    private readonly TextEditor diff = CodeEditorView.Create(showLineNumbers: false);
    private readonly TextBlock sourceTitle = new() { Text = "No file open", FontWeight = FontWeight.SemiBold };
    private readonly TextBlock gitSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock dotNetSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button open = new() { Content = "Open file" };
    private readonly Button search = new() { Content = "Search" };
    private readonly Button refresh = new() { Content = "Refresh Git and .NET" };
    private bool busy;

    internal WorkspaceInspectorDialog(
        IWorkspaceInspectionService inspectionService,
        WorkspaceView workspace,
        CancellationToken cancellationToken)
    {
        this.inspectionService = inspectionService;
        this.workspace = workspace;
        this.cancellationToken = cancellationToken;
        Title = $"Inspect {workspace.Name}";
        Width = 1100;
        Height = 760;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        Opened += async (_, _) => await RefreshMetadataAsync();
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new("Auto,*,Auto,Auto"),
            Margin = new Thickness(18),
            RowSpacing = 10,
        };
        Grid heading = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        heading.Children.Add(new TextBlock
        {
            Text = $"{workspace.RootPath}  ·  {workspace.Branch}",
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(refresh, 1);
        heading.Children.Add(refresh);
        root.Children.Add(heading);

        TabControl tabs = new()
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "Files", Content = BuildFilesTab() },
                new TabItem { Header = "Git", Content = BuildGitTab() },
                new TabItem { Header = ".NET", Content = BuildDotNetTab() },
            },
        };
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        Grid.SetRow(status, 2);
        root.Children.Add(status);
        Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 3);
        root.Children.Add(close);
        return root;
    }

    private Control BuildFilesTab()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new("320,*"),
            RowDefinitions = new("Auto,Auto,*"),
            ColumnSpacing = 12,
            RowSpacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid pathRow = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        path.PlaceholderText = "Relative path, for example src/App.cs";
        AutomationProperties.SetName(path, "Workspace-relative file path");
        pathRow.Children.Add(path);
        Grid.SetColumn(open, 1);
        pathRow.Children.Add(open);
        grid.Children.Add(pathRow);

        Grid searchRow = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        query.PlaceholderText = "Search tracked text";
        AutomationProperties.SetName(query, "Search tracked workspace text");
        searchRow.Children.Add(query);
        Grid.SetColumn(search, 1);
        searchRow.Children.Add(search);
        Grid.SetRow(searchRow, 1);
        grid.Children.Add(searchRow);

        AutomationProperties.SetName(results, "Workspace text search results");
        Grid.SetRow(results, 2);
        grid.Children.Add(results);

        Grid editor = new() { RowDefinitions = new("Auto,*"), RowSpacing = 6 };
        editor.Children.Add(sourceTitle);
        Grid.SetRow(source, 1);
        editor.Children.Add(source);
        Grid.SetColumn(editor, 1);
        Grid.SetRowSpan(editor, 3);
        grid.Children.Add(editor);
        return grid;
    }

    private Control BuildGitTab()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,150,*"),
            RowSpacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
        };
        grid.Children.Add(gitSummary);
        AutomationProperties.SetName(changes, "Git working tree changes");
        Grid.SetRow(changes, 1);
        grid.Children.Add(changes);
        Grid.SetRow(diff, 2);
        grid.Children.Add(diff);
        return grid;
    }

    private Control BuildDotNetTab()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*"),
            RowSpacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
        };
        grid.Children.Add(dotNetSummary);
        AutomationProperties.SetName(projects, ".NET projects and references");
        Grid.SetRow(projects, 1);
        grid.Children.Add(projects);
        return grid;
    }

    private void WireInteractions()
    {
        open.Click += async (_, _) => await OpenFileAsync(path.Text ?? string.Empty);
        path.KeyDown += async (_, args) =>
        {
            if (args.Key is Key.Enter)
            {
                args.Handled = true;
                await OpenFileAsync(path.Text ?? string.Empty);
            }
        };
        search.Click += async (_, _) => await SearchAsync();
        query.KeyDown += async (_, args) =>
        {
            if (args.Key is Key.Enter)
            {
                args.Handled = true;
                await SearchAsync();
            }
        };
        results.DoubleTapped += async (_, _) =>
        {
            if (results.SelectedItem is SearchChoice choice)
            {
                path.Text = choice.Match.Path;
                await OpenFileAsync(choice.Match.Path);
            }
        };
        changes.DoubleTapped += async (_, _) =>
        {
            if (changes.SelectedItem is ChangeChoice choice)
            {
                path.Text = choice.Change.Path;
                await OpenFileAsync(choice.Change.Path);
            }
        };
        refresh.Click += async (_, _) => await RefreshMetadataAsync();
    }

    private async Task OpenFileAsync(string relativePath)
    {
        if (busy || string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        await RunAsync(async () =>
        {
            WorkspaceFileView file = await inspectionService.ReadFileAsync(
                workspace.Id,
                relativePath.Trim(),
                cancellationToken);
            if (file.Error is not null)
            {
                status.Text = file.Error;
                return;
            }

            source.Text = file.Content;
            source.SyntaxHighlighting = AvaloniaEdit.Highlighting.HighlightingManager.Instance
                .GetDefinitionByExtension(Path.GetExtension(file.Path));
            sourceTitle.Text = $"{file.Path}  ·  {file.SizeBytes:N0} bytes" +
                               (file.IsTruncated ? "  ·  truncated" : string.Empty);
            status.Text = $"Opened {file.Path}.";
        });
    }

    private async Task SearchAsync()
    {
        if (busy || string.IsNullOrWhiteSpace(query.Text))
        {
            return;
        }

        await RunAsync(async () =>
        {
            WorkspaceTextSearchView result = await inspectionService.SearchTextAsync(
                workspace.Id,
                query.Text.Trim(),
                cancellationToken);
            results.ItemsSource = result.Matches.Select(match => new SearchChoice(match)).ToArray();
            status.Text = result.Error ??
                          $"{result.Matches.Count} match(es) in {result.FilesScanned} file(s)" +
                          (result.IsTruncated ? " · results truncated" : string.Empty) + ".";
        });
    }

    private async Task RefreshMetadataAsync()
    {
        if (busy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            WorkspaceGitStateView git = await inspectionService.InspectGitAsync(
                workspace.Id,
                cancellationToken);
            WorkspaceDotNetInfoView dotnet = await inspectionService.InspectDotNetAsync(
                workspace.Id,
                cancellationToken);
            gitSummary.Text = git.Error ??
                              $"Branch {git.Branch}  ·  HEAD {git.HeadSha ?? "unborn"}  ·  " +
                              $"{git.Changes.Count} change(s)" +
                              (git.IsTruncated ? "  ·  truncated" : string.Empty);
            changes.ItemsSource = git.Changes.Select(change => new ChangeChoice(change)).ToArray();
            diff.Text = git.Diff;
            dotNetSummary.Text = dotnet.Error ??
                                 $"{dotnet.EntryPointKind}: {dotnet.EntryPoint}\n" +
                                 $"{dotnet.Projects.Count} project(s)" +
                                 (dotnet.SdkPolicy?.Version is { } version ? $" · SDK {version}" : string.Empty) +
                                 (dotnet.IsTruncated ? " · truncated" : string.Empty);
            projects.ItemsSource = dotnet.Projects.Select(project => new ProjectChoice(project)).ToArray();
            status.Text = git.Error ?? dotnet.Error ?? "Workspace inspection refreshed.";
        });
    }

    private async Task RunAsync(Func<Task> operation)
    {
        busy = true;
        SetEnabled(false);
        status.Text = "Inspecting workspace…";
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            status.Text = "Workspace inspection cancelled.";
        }
        catch (Exception exception)
        {
            status.Text = exception.Message;
        }
        finally
        {
            busy = false;
            SetEnabled(true);
        }
    }

    private void SetEnabled(bool enabled)
    {
        open.IsEnabled = enabled;
        search.IsEnabled = enabled;
        refresh.IsEnabled = enabled;
    }

    private sealed record SearchChoice(WorkspaceTextMatchView Match)
    {
        public override string ToString() => $"{Match.Path}:{Match.LineNumber}  {Match.Text}";
    }

    private sealed record ChangeChoice(WorkspaceGitFileChangeView Change)
    {
        public override string ToString() => $"{Change.Status}  {Change.Path}";
    }

    private sealed record ProjectChoice(DotNetProjectView Project)
    {
        public override string ToString()
        {
            string frameworks = Project.TargetFrameworks.Count == 0
                ? "framework not declared"
                : string.Join(", ", Project.TargetFrameworks);
            return $"{Project.Path}  ·  {frameworks}  ·  {Project.References.Count} reference(s)";
        }
    }
}
