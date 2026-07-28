using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;
using DockAlignment = Dock.Model.Core.Alignment;
using DockOrientation = Dock.Model.Core.Orientation;
using AvaloniaOrientation = Avalonia.Layout.Orientation;

namespace Harness.Presentation.Avalonia;

internal sealed class WorkbenchDockHost
{
    private const string OverviewDocumentId = "document.workspace.overview";
    private const string DiffDocumentId = "document.git.diff";
    private const string PlanDocumentId = "document.goal.plan";
    private const string EvidenceDocumentId = "document.goal.evidence";

    private readonly IWorkspaceInspectionService inspectionService;
    private readonly Func<AvaloniaShellState> state;
    private readonly CancellationToken cancellationToken;
    private readonly Factory factory = new();
    private readonly IDocumentDock documents;
    private readonly IDockable overviewDocument;
    private readonly TextBlock overviewHeading = new()
    {
        FontSize = 22,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock overviewDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox path = new();
    private readonly TextBox query = new();
    private readonly ListBox searchResults = new();
    private readonly TextBlock fileStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox changes = new();
    private readonly TextBlock gitSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock gitStatus = new() { TextWrapping = TextWrapping.Wrap };
    private string? workspaceId;
    private bool busy;

    internal WorkbenchDockHost(
        IWorkspaceInspectionService inspectionService,
        Func<AvaloniaShellState> state,
        Control navigation,
        Control conversation,
        Control goalContext,
        CancellationToken cancellationToken)
    {
        this.inspectionService = inspectionService;
        this.state = state;
        this.cancellationToken = cancellationToken;

        Control files = BuildFilesTool();
        Control sourceControl = BuildSourceControlTool();
        Control context = BuildContextTool(goalContext);
        Control overviewContent = BuildOverviewDocument();

        factory
            .Tool(out ITool? navigationTool, item => item
                .WithId("tool.navigation")
                .WithTitle("Workspace")
                .WithCanClose(false)
                .WithContext(navigation))
            .Tool(out ITool? filesTool, item => item
                .WithId("tool.files")
                .WithTitle("Files")
                .WithCanClose(false)
                .WithContext(files))
            .Tool(out ITool? contextTool, item => item
                .WithId("tool.context")
                .WithTitle("Goal context")
                .WithCanClose(false)
                .WithContext(context))
            .Tool(out ITool? gitTool, item => item
                .WithId("tool.git")
                .WithTitle("Git")
                .WithCanClose(false)
                .WithContext(sourceControl))
            .Tool(out ITool? conversationTool, item => item
                .WithId("tool.conversation")
                .WithTitle("Conversation")
                .WithCanClose(false)
                .WithContext(conversation))
            .Document(out IDocument? overview, item => item
                .WithId(OverviewDocumentId)
                .WithTitle("Workspace overview")
                .WithCanClose(false)
                .WithCanFloat(false)
                .WithContext(overviewContent))
            .DocumentDock(out IDocumentDock? documentDock, dock => dock
                .WithId("dock.documents")
                .WithTitle("Editor")
                .WithIsCollapsable(false)
                .WithCanCloseLastDockable(false)
                .WithCanCreateDocument(false))
            .ToolDock(out IToolDock left, DockAlignment.Left, dock => dock
                .WithId("dock.left")
                .AppendTool(navigationTool!)
                .AppendTool(filesTool!)
                .WithActiveDockable(filesTool))
            .ToolDock(out IToolDock right, DockAlignment.Right, dock => dock
                .WithId("dock.right")
                .AppendTool(contextTool!)
                .AppendTool(gitTool!)
                .WithActiveDockable(contextTool))
            .ToolDock(out IToolDock bottom, DockAlignment.Bottom, dock => dock
                .WithId("dock.bottom")
                .AppendTool(conversationTool!)
                .WithActiveDockable(conversationTool))
            .ProportionalDockSplitter(out IProportionalDockSplitter leftSplitter)
            .ProportionalDockSplitter(out IProportionalDockSplitter rightSplitter)
            .ProportionalDockSplitter(out IProportionalDockSplitter bottomSplitter)
            .ProportionalDock(out IProportionalDock center, DockOrientation.Vertical, dock => dock
                .Add(documentDock!, bottomSplitter!, bottom!))
            .ProportionalDock(out IProportionalDock workbench, DockOrientation.Horizontal, dock => dock
                .Add(left!, leftSplitter!, center!, rightSplitter!, right!))
            .RootDock(out IRootDock root, dock => dock
                .Add(workbench!)
                .WithDefaultDockable(workbench)
                .WithActiveDockable(workbench));

        documents = documentDock ?? throw new InvalidOperationException("Dock did not create the document region.");
        overviewDocument = overview ?? throw new InvalidOperationException("Dock did not create the overview document.");
        left!.WithProportion(0.19);
        right!.WithProportion(0.22);
        bottom!.WithProportion(0.32);
        factory.InitLayout(root!);
        documents.AddDocument(overviewDocument);

        Control = new DockControl
        {
            Factory = factory,
            Layout = root,
        };
        AutomationProperties.SetName(Control, "Docked workspace workbench");
    }

    internal DockControl Control { get; }
    internal IDocumentDock Documents => documents;

    internal async ValueTask RefreshAsync()
    {
        Update(state());
        if (ActiveWorkspace() is { IsTrusted: true })
        {
            await RefreshGitAsync();
        }
    }

    internal void Update(AvaloniaShellState snapshot)
    {
        WorkspaceView? active = snapshot.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        if (!string.Equals(workspaceId, active?.Id, StringComparison.Ordinal))
        {
            workspaceId = active?.Id;
            CloseWorkspaceDocuments();
            searchResults.ItemsSource = Array.Empty<SearchChoice>();
            changes.ItemsSource = Array.Empty<ChangeChoice>();
            fileStatus.Text = string.Empty;
            gitStatus.Text = string.Empty;
            gitSummary.Text = active is null ? "No workspace selected." : "Refresh Git state.";
        }

        if (active is null)
        {
            overviewHeading.Text = "No workspace selected";
            overviewDetails.Text = "Register and trust a Git-backed .NET repository to open real files, diffs, plans, and evidence.";
            return;
        }

        overviewHeading.Text = active.Name;
        overviewDetails.Text = $"{active.RootPath}\n\nBranch: {active.Branch}\n" +
                               $"Trust: {(active.IsTrusted ? "Trusted" : "Not trusted")}\n" +
                               $"Working tree: {(active.IsDirty ? "Has changes" : "Clean")}\n\n" +
                               (active.IsTrusted
                                   ? "Use Files or Git to open source and diff documents in this editor."
                                   : "Trust this workspace before reading repository content.");
    }

    internal async ValueTask OpenFileAsync(string relativePath)
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted || string.IsNullOrWhiteSpace(relativePath))
        {
            fileStatus.Text = active is null
                ? "Select a workspace first."
                : active.IsTrusted ? "Enter a relative file path." : "Trust the workspace before reading files.";
            return;
        }

        await RunAsync(async () =>
        {
            WorkspaceFileView file = await inspectionService.ReadFileAsync(
                active.Id,
                relativePath.Trim(),
                cancellationToken);
            if (file.Error is not null)
            {
                fileStatus.Text = file.Error;
                return;
            }

            string id = $"document.file.{active.Id}.{file.Path}";
            IDockable document = OpenOrReplaceDocument(
                id,
                Path.GetFileName(file.Path),
                CreateEditor(file.Content, file.Path, showLineNumbers: true));
            document.Title = file.IsTruncated ? $"{Path.GetFileName(file.Path)} · truncated" : Path.GetFileName(file.Path);
            fileStatus.Text = $"Opened {file.Path} · {file.SizeBytes:N0} bytes" +
                              (file.IsTruncated ? " · truncated." : ".");
        });
    }

    internal async ValueTask RefreshGitAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted)
        {
            gitStatus.Text = active is null
                ? "Select a workspace first."
                : "Trust the workspace before inspecting Git.";
            return;
        }

        await RunAsync(async () =>
        {
            WorkspaceGitStateView git = await inspectionService.InspectGitAsync(active.Id, cancellationToken);
            if (git.Error is not null)
            {
                gitStatus.Text = git.Error;
                return;
            }

            gitSummary.Text = $"Branch {git.Branch}\nHEAD {git.HeadSha ?? "unborn"}\n" +
                              $"{git.Changes.Count} change(s)" +
                              (git.IsTruncated ? " · truncated" : string.Empty);
            changes.ItemsSource = git.Changes.Select(change => new ChangeChoice(change)).ToArray();
            gitStatus.Text = "Git state refreshed.";
        });
    }

    internal async ValueTask OpenDiffAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted)
        {
            gitStatus.Text = active is null
                ? "Select a workspace first."
                : "Trust the workspace before inspecting Git.";
            return;
        }

        await RunAsync(async () =>
        {
            WorkspaceGitStateView git = await inspectionService.InspectGitAsync(active.Id, cancellationToken);
            if (git.Error is not null)
            {
                gitStatus.Text = git.Error;
                return;
            }

            if (string.IsNullOrWhiteSpace(git.Diff))
            {
                gitStatus.Text = "The working tree has no textual diff.";
                return;
            }

            OpenOrReplaceDocument(
                DiffDocumentId,
                $"{git.Branch} working diff",
                CreateEditor(git.Diff, "workspace.diff", showLineNumbers: false));
            gitStatus.Text = "Opened the current bounded Git diff.";
        });
    }

    internal void OpenPlan()
    {
        if (state().Goals.CurrentPlan is not { } plan)
        {
            overviewDetails.Text = "The selected goal has no current plan to open.";
            ActivateOverview();
            return;
        }

        OpenOrReplaceDocument(
            PlanDocumentId,
            $"Plan · revision {plan.Revision.Value}",
            new ScrollViewer
            {
                Content = MarkdownContentView.Create(plan.Content, _ => null),
                Padding = new Thickness(18),
            });
    }

    internal void OpenEvidence()
    {
        if (state().Goals.Workflow?.Evidence is not { Count: > 0 } items)
        {
            overviewDetails.Text = "The selected goal has no durable workflow evidence to open.";
            ActivateOverview();
            return;
        }

        StackPanel content = new() { Spacing = 14 };
        foreach (var item in items)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{item.Sequence}. {item.Title.Value}",
                FontWeight = FontWeight.SemiBold,
            });
            content.Children.Add(MarkdownContentView.Create(item.Content.Value, _ => null));
        }

        OpenOrReplaceDocument(
            EvidenceDocumentId,
            "Workflow evidence",
            new ScrollViewer { Content = content, Padding = new Thickness(18) });
    }

    private Control BuildFilesTool()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,Auto,*,Auto"),
            Margin = new Thickness(10),
            RowSpacing = 8,
        };
        Grid pathRow = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        path.PlaceholderText = "Relative file path";
        AutomationProperties.SetName(path, "Workspace-relative file path");
        Button open = new() { Content = "Open" };
        open.Click += async (_, _) => await OpenFileAsync(path.Text ?? string.Empty);
        path.KeyDown += async (_, args) =>
        {
            if (args.Key is Key.Enter)
            {
                args.Handled = true;
                await OpenFileAsync(path.Text ?? string.Empty);
            }
        };
        pathRow.Children.Add(path);
        Grid.SetColumn(open, 1);
        pathRow.Children.Add(open);
        grid.Children.Add(pathRow);

        Grid searchRow = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        query.PlaceholderText = "Search tracked text";
        AutomationProperties.SetName(query, "Search tracked workspace text");
        Button search = new() { Content = "Search" };
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
        Grid.SetRow(searchRow, 1);
        grid.Children.Add(searchRow);

        AutomationProperties.SetName(searchResults, "Tracked-text search results");
        searchResults.DoubleTapped += async (_, _) =>
        {
            if (searchResults.SelectedItem is SearchChoice choice)
            {
                path.Text = choice.Match.Path;
                await OpenFileAsync(choice.Match.Path);
            }
        };
        Grid.SetRow(searchResults, 2);
        grid.Children.Add(searchResults);
        Grid.SetRow(fileStatus, 3);
        grid.Children.Add(fileStatus);
        return grid;
    }

    private Control BuildSourceControlTool()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,Auto,*,Auto"),
            Margin = new Thickness(10),
            RowSpacing = 8,
        };
        grid.Children.Add(gitSummary);
        StackPanel actions = new()
        {
            Orientation = AvaloniaOrientation.Horizontal,
            Spacing = 6,
        };
        Button refresh = new() { Content = "Refresh" };
        refresh.Click += async (_, _) => await RefreshGitAsync();
        Button openDiff = new() { Content = "Open diff" };
        openDiff.Click += async (_, _) => await OpenDiffAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(openDiff);
        Grid.SetRow(actions, 1);
        grid.Children.Add(actions);
        AutomationProperties.SetName(changes, "Git working-tree changes");
        changes.DoubleTapped += async (_, _) =>
        {
            if (changes.SelectedItem is ChangeChoice choice)
            {
                path.Text = choice.Change.Path;
                await OpenFileAsync(choice.Change.Path);
            }
        };
        Grid.SetRow(changes, 2);
        grid.Children.Add(changes);
        Grid.SetRow(gitStatus, 3);
        grid.Children.Add(gitStatus);
        return grid;
    }

    private Control BuildContextTool(Control context)
    {
        Grid grid = new() { RowDefinitions = new("*,Auto"), RowSpacing = 8 };
        grid.Children.Add(context);
        StackPanel actions = new()
        {
            Orientation = AvaloniaOrientation.Horizontal,
            Margin = new Thickness(10),
            Spacing = 6,
        };
        Button plan = new() { Content = "Open plan" };
        plan.Click += (_, _) => OpenPlan();
        Button evidence = new() { Content = "Open evidence" };
        evidence.Click += (_, _) => OpenEvidence();
        actions.Children.Add(plan);
        actions.Children.Add(evidence);
        Grid.SetRow(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private Control BuildOverviewDocument() => new ScrollViewer
    {
        Content = new StackPanel
        {
            Margin = new Thickness(30),
            MaxWidth = 720,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { overviewHeading, overviewDetails },
        },
    };

    private async ValueTask SearchAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted || string.IsNullOrWhiteSpace(query.Text))
        {
            fileStatus.Text = active is null
                ? "Select a workspace first."
                : active.IsTrusted ? "Enter text to search." : "Trust the workspace before searching files.";
            return;
        }

        await RunAsync(async () =>
        {
            WorkspaceTextSearchView result = await inspectionService.SearchTextAsync(
                active.Id,
                query.Text.Trim(),
                cancellationToken);
            searchResults.ItemsSource = result.Matches.Select(match => new SearchChoice(match)).ToArray();
            fileStatus.Text = result.Error ??
                              $"{result.Matches.Count} match(es) in {result.FilesScanned} file(s)" +
                              (result.IsTruncated ? " · truncated." : ".");
        });
    }

    private IDockable OpenOrReplaceDocument(string id, string title, Control content)
    {
        IDockable? existing = documents.VisibleDockables?.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Title = title;
            existing.Context = content;
            factory.SetActiveDockable(existing);
            return existing;
        }

        factory.Document(out IDocument? document, item => item
            .WithId(id)
            .WithTitle(title)
            .WithCanClose(true)
            .WithCanFloat(true)
            .WithContext(content));
        IDocument created = document ?? throw new InvalidOperationException("Dock did not create the document.");
        documents.AddDocument(created);
        factory.SetActiveDockable(created);
        return created;
    }

    private static Control CreateEditor(string content, string path, bool showLineNumbers)
    {
        TextEditor editor = CodeEditorView.Create(
            content,
            isReadOnly: true,
            wordWrap: false,
            showLineNumbers: showLineNumbers,
            path: path);
        AutomationProperties.SetName(editor, $"Read-only editor for {path}");
        return editor;
    }

    private void CloseWorkspaceDocuments()
    {
        foreach (IDockable document in documents.VisibleDockables?
                     .Where(item => !string.Equals(item.Id, OverviewDocumentId, StringComparison.Ordinal))
                     .ToArray() ?? [])
        {
            factory.CloseDockable(document);
        }

        ActivateOverview();
    }

    private void ActivateOverview() => factory.SetActiveDockable(overviewDocument);

    private WorkspaceView? ActiveWorkspace() =>
        state().Workspaces.Registered.FirstOrDefault(item => item.IsActive);

    private async ValueTask RunAsync(Func<ValueTask> operation)
    {
        busy = true;
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            fileStatus.Text = "Workspace operation cancelled.";
            gitStatus.Text = "Workspace operation cancelled.";
        }
        catch (Exception exception)
        {
            fileStatus.Text = exception.Message;
            gitStatus.Text = exception.Message;
        }
        finally
        {
            busy = false;
        }
    }

    private sealed record SearchChoice(WorkspaceTextMatchView Match)
    {
        public override string ToString() => $"{Match.Path}:{Match.LineNumber}  {Match.Text}";
    }

    private sealed record ChangeChoice(WorkspaceGitFileChangeView Change)
    {
        public override string ToString() => $"{Change.Status}  {Change.Path}";
    }
}
