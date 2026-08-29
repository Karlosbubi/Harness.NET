using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Empty_workbench_offers_a_direct_workspace_folder_action()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            bool requested = false;
            bool browseImmediately = false;
            WorkbenchDockHost workbench = CreateWorkbench(
                AvaloniaShellState.Initial with { IsLoading = false },
                new(),
                manageWorkspace: browse =>
                {
                    requested = true;
                    browseImmediately = browse;
                    return Task.CompletedTask;
                });
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.Update(AvaloniaShellState.Initial with { IsLoading = false });

            Button action = workbench.OverviewAction;
            Assert.Equal("Open workspace", action.Content);
            Assert.Contains("primary", action.Classes);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(requested);
            Assert.True(browseImmediately);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Docked_workbench_opens_real_workspace_file_as_center_document()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkspaceView workspace = new(
                "workspace-1",
                "/work/repository",
                "repository",
                "/work/repository/Harness.slnx",
                IsTrusted: true,
                IsActive: true,
                "main",
                IsDirty: true);
            AvaloniaShellState shell = AvaloniaShellState.Initial with
            {
                Workspaces = WorkspaceManagementState.Initial with { Registered = [workspace] },
                IsLoading = false,
            };
            DocumentService documents = new();
            WorkbenchDockHost workbench = new(
                new RunOutputService(),
                new InspectionService(),
                documents,
                new CodeIntelligenceService(),
                new LayoutService(),
                new DocumentPrompt(),
                () => shell,
                new TextBlock { Text = "Workspace" },
                new TextBlock { Text = "Conversation" },
                new TextBlock { Text = "Goal context" },
                CancellationToken.None);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.OpenFileAsync("src/Feature.cs").AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            using Bitmap rendered = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());

            Assert.IsType<DockControl>(workbench.Control);
            Assert.Equal(
                ["document.workspace.overview", "document.file.workspace-1.original.src/App.cs",
                    "document.file.workspace-1.original.src/Feature.cs"],
                workbench.Documents.VisibleDockables?.Select(item => item.Id).ToArray() ?? []);
            DocumentTabStripItem[] documentTabs = window.GetVisualDescendants()
                .OfType<DocumentTabStripItem>()
                .ToArray();
            Assert.Equal(3, documentTabs.Length);
            Assert.Equal(
                ["Workspace overview", "App.cs", "Feature.cs"],
                documentTabs.Select(tab => AutomationProperties.GetName(tab) ?? string.Empty).ToArray());
            Assert.All(documentTabs, tab => Assert.Equal(
                AccessibilityView.Content,
                AutomationProperties.GetAccessibilityView(tab)));
            ComboBox documentSwitcher = workbench.DocumentSwitcher;
            Assert.Equal("Open editor documents", AutomationProperties.GetName(documentSwitcher));
            Assert.Equal(
                ["Workspace overview", "App.cs", "Feature.cs"],
                Assert.IsAssignableFrom<IEnumerable<object>>(documentSwitcher.ItemsSource)
                    .Select(item => item.ToString() ?? string.Empty)
                    .ToArray());
            documentSwitcher.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("App.cs", workbench.Documents.ActiveDockable?.Title);
            Button focusEditor = Assert.Single(
                Assert.IsType<StackPanel>(workbench.DocumentActions).Children.OfType<Button>());
            Assert.Equal(
                "Focus the active editor document",
                AutomationProperties.GetName(focusEditor));
            focusEditor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(workbench.ActiveSourceEditor, workbench.LastRequestedFocusTarget);
            Assert.Equal(7, DurableTools(workbench.Root).Count);
            Control documentContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            TextEditor editor = Assert.Single(
                documentContent.GetVisualDescendants().OfType<TextEditor>());
            Assert.Contains(editor, window.GetVisualDescendants().OfType<TextEditor>());
            Assert.Equal("namespace Example;", editor.Text);
            Assert.False(editor.IsReadOnly);
            string sourceChrome = string.Join('\n', documentContent.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text));
            Assert.Contains("src › App.cs", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("Original workspace", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("EDITABLE", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("Ln 1, Col 1 · UTF-8 · No line break", sourceChrome, StringComparison.Ordinal);
            editor.Text = "namespace UserEdited;";
            Assert.True(workbench.SaveActiveSourceDocumentAsync().AsTask().GetAwaiter().GetResult());
            WorkbenchDocumentSaveRequest save = Assert.Single(documents.SaveRequests);
            Assert.Equal("workspace-1", save.WorkspaceId.Value);
            Assert.Null(save.GoalId);
            Assert.Equal("src/App.cs", save.Path.Value);
            Assert.Equal("namespace UserEdited;", save.Content.Value);
            Assert.NotNull(workbench.Control.Template);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Approved_goal_document_tracks_real_dirty_state_and_saves_with_its_baseline()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            TextEditor editor = Assert.IsType<TextEditor>(workbench.ActiveSourceEditor);
            Assert.False(editor.IsReadOnly);
            Assert.Contains("Editable source editor", AutomationProperties.GetName(editor), StringComparison.Ordinal);
            editor.Text = "namespace Changed;";

            Assert.True(workbench.ActiveSourceDocumentIsDirty);
            Assert.True(workbench.Documents.ActiveDockable?.IsModified);
            Control sourceContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Button[] documentActions = sourceContent.GetVisualDescendants()
                .OfType<Button>()
                .ToArray();
            Assert.Equal(
                ["Save", "Reload", "Close", "CodeLens", "Outline", "Symbols", "IntelliSense", "Symbol info", "Definition",
                    "Usages", "Implementations", "Inspect", "Quick fix…", "Transform"],
                documentActions.Select(item => item.Content?.ToString() ?? string.Empty).ToArray());
            Assert.All(documentActions, item => Assert.False(
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(item))));
            Assert.True(documentActions[0].IsEnabled);
            string sourceChrome = string.Join('\n', sourceContent.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text));
            Assert.Contains("src › App.cs", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("harness/goal-1", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("EDITABLE", sourceChrome, StringComparison.Ordinal);
            editor.Text = "one\ntwo";
            editor.CaretOffset = editor.Text.Length;
            Dispatcher.UIThread.RunJobs();
            sourceChrome = string.Join('\n', sourceContent.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text));
            Assert.Contains("Ln 2, Col 4 · UTF-8 · LF", sourceChrome, StringComparison.Ordinal);
            editor.Text = "namespace Changed;";
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.S,
                KeyModifiers = KeyModifiers.Control,
            });

            WorkbenchDocumentSaveRequest request = Assert.Single(documents.SaveRequests);
            Assert.Equal("goal-1", request.GoalId!.Value);
            Assert.Equal("src/App.cs", request.Path.Value);
            Assert.Equal("namespace Changed;", request.Content.Value);
            Assert.Equal(
                "7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4",
                request.ExpectedSha256?.Value);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            Assert.False(workbench.Documents.ActiveDockable?.IsModified);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dirty_document_switch_requires_cancel_or_discard_before_activation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents, prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            workbench.OpenFileAsync("src/Other.cs").AsTask().GetAwaiter().GetResult();
            Assert.Equal(1, workbench.SourceDocumentCount);
            Assert.Contains("src/App.cs", workbench.Documents.ActiveDockable?.Id, StringComparison.Ordinal);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            workbench.OpenFileAsync("src/Other.cs").AsTask().GetAwaiter().GetResult();
            Assert.Equal(2, workbench.SourceDocumentCount);
            Assert.Contains("src/Other.cs", workbench.Documents.ActiveDockable?.Id, StringComparison.Ordinal);
            Assert.All(
                prompt.UnsavedPrompts,
                item => Assert.Equal(WorkbenchDocumentTransition.Switch, item.Transition));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dock_tab_activation_cannot_bypass_dirty_document_decisions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents, prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            IDockable app = Assert.Single(
                workbench.Documents.VisibleDockables!,
                item => item.Id?.Contains("src/App.cs", StringComparison.Ordinal) is true);
            workbench.OpenFileAsync("src/Other.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved other";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            workbench.Factory.SetActiveDockable(app);
            Assert.Contains("src/Other.cs", workbench.Documents.ActiveDockable?.Id, StringComparison.Ordinal);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Save);
            workbench.Factory.SetActiveDockable(app);
            Assert.Same(app, workbench.Documents.ActiveDockable);
            Assert.Single(documents.SaveRequests);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Save_conflict_requires_explicit_overwrite_and_retries_against_the_observed_version()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            string current = new('c', 64);
            documents.SaveResults.Enqueue(new(
                new("workspace-1"),
                new("goal-1"),
                new("ignored-1"),
                new("src/App.cs"),
                new(new string('7', 64)),
                new(current),
                null,
                new(0),
                WorkbenchDocumentSaveOutcome.Conflict,
                "content_changed",
                "The file changed."));
            documents.SaveResults.Enqueue(new(
                new("workspace-1"),
                new("goal-1"),
                new("ignored-2"),
                new("src/App.cs"),
                new(current),
                new(current),
                new(new string('d', 64)),
                new(18),
                WorkbenchDocumentSaveOutcome.Saved,
                null,
                null));
            DocumentPrompt prompt = new();
            prompt.ConflictDecisions.Enqueue(WorkbenchConflictDecision.Overwrite);
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents, prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "namespace Changed;";

            Assert.True(workbench.SaveActiveSourceDocumentAsync().AsTask().GetAwaiter().GetResult());

            Assert.Equal(2, documents.SaveRequests.Count);
            Assert.Equal(current, documents.SaveRequests[1].ExpectedSha256?.Value);
            Assert.Single(prompt.ConflictPrompts);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dirty_close_and_application_exit_honor_save_discard_cancel_decisions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents, prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            workbench.ActiveSourceEditor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.W,
                KeyModifiers = KeyModifiers.Control,
            });
            Assert.Equal(1, workbench.SourceDocumentCount);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            Assert.False(workbench.PrepareForShutdownAsync().AsTask().GetAwaiter().GetResult());
            Assert.Equal(1, workbench.SourceDocumentCount);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            workbench.CloseActiveSourceDocumentAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal(0, workbench.SourceDocumentCount);
            Assert.Equal(
                [WorkbenchDocumentTransition.Close, WorkbenchDocumentTransition.Exit,
                    WorkbenchDocumentTransition.Close],
                prompt.UnsavedPrompts.Select(item => item.Transition).ToArray());
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dock_close_chrome_cannot_remove_a_dirty_document_without_a_decision()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentPrompt prompt = new();
            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            WorkbenchDockHost workbench = CreateWorkbench(
                shell,
                new(),
                new() { Editable = true },
                prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved";

            workbench.Factory.CloseDockable(workbench.Documents.ActiveDockable!);
            Assert.Equal(1, workbench.SourceDocumentCount);
            Dispatcher.UIThread.RunJobs();
            WorkbenchUnsavedPrompt close = Assert.Single(prompt.UnsavedPrompts);
            Assert.Equal(WorkbenchDocumentTransition.Close, close.Transition);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);
            Assert.Equal(1, workbench.SourceDocumentCount);
            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// The diff document renders decorated rows rather than one raw editor, so its content is
    /// read back from the rendered text rows.
    /// </summary>
    private static string RenderedDiffText(Window window, IDockable diff)
    {
        window.UpdateLayout();
        Control content = Assert.IsAssignableFrom<Control>(diff.Context);
        return string.Join(
            '\n',
            content.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text));
    }

    [Fact]
    public async Task Approved_goal_source_and_diff_share_context_and_keep_document_identity()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            InspectionService inspection = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                shell,
                new(),
                new() { Editable = true },
                inspection: inspection);
            Window window = new() { Content = workbench.Control };
            window.Show();

            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            IDockable source = workbench.Documents.ActiveDockable!;
            TextEditor sourceEditor = workbench.ActiveSourceEditor!;
            workbench.OpenDiffAsync().AsTask().GetAwaiter().GetResult();

            IDockable diff = workbench.Documents.ActiveDockable!;
            Assert.Equal("document.git.diff.workspace-1.goal-1", diff.Id);
            Assert.Equal("harness/goal-1 working diff", diff.Title);
            Assert.Contains("first diff", RenderedDiffText(window, diff), StringComparison.Ordinal);
            Assert.All(inspection.Requests, request => Assert.Equal("goal-1", request.GoalId?.Value));

            inspection.Diff = "refreshed diff";
            workbench.OpenDiffAsync().AsTask().GetAwaiter().GetResult();
            Assert.Same(diff, workbench.Documents.ActiveDockable);
            Assert.Contains("refreshed diff", RenderedDiffText(window, diff), StringComparison.Ordinal);

            workbench.Factory.SetActiveDockable(source);
            Assert.Same(sourceEditor, workbench.ActiveSourceEditor);
            workbench.Factory.SetActiveDockable(diff);
            workbench.Factory.CloseDockable(diff);
            Assert.DoesNotContain(workbench.Documents.VisibleDockables!, item => item.Id == diff.Id);
            workbench.Factory.SetActiveDockable(source);
            Assert.Same(sourceEditor, workbench.ActiveSourceEditor);
            Assert.Equal("namespace Example;", sourceEditor.Text);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Representative_multi_project_tabs_retain_cached_editors_during_switching()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true });
            Window window = new() { Content = workbench.Control };
            window.Show();
            string[] paths = Enumerable.Range(1, 6)
                .SelectMany(project => new[]
                {
                    $"src/Project{project}/Program.cs",
                    $"src/Project{project}/Services/Worker.cs",
                    $"tests/Project{project}.Tests/WorkerTests.cs",
                })
                .ToArray();
            Dictionary<string, TextEditor> editors = new(StringComparer.Ordinal);
            Stopwatch opening = Stopwatch.StartNew();
            foreach (string item in paths)
            {
                workbench.OpenFileAsync(item).AsTask().GetAwaiter().GetResult();
                editors.Add(workbench.Documents.ActiveDockable!.Id!, workbench.ActiveSourceEditor!);
            }

            opening.Stop();
            IDockable[] documents = workbench.Documents.VisibleDockables!
                .Where(item => item.Id?.StartsWith("document.file.", StringComparison.Ordinal) is true)
                .ToArray();
            Stopwatch switching = Stopwatch.StartNew();
            for (int pass = 0; pass < 100; pass++)
            {
                foreach (IDockable document in documents)
                {
                    workbench.Factory.SetActiveDockable(document);
                    Assert.Same(editors[document.Id!], workbench.ActiveSourceEditor);
                }
            }

            switching.Stop();
            Assert.Equal(18, documents.Length);
            Assert.True(opening.Elapsed < TimeSpan.FromSeconds(10),
                $"Opening 18 representative documents took {opening.Elapsed}.");
            Assert.True(switching.Elapsed < TimeSpan.FromSeconds(5),
                $"Switching 1,800 cached tabs took {switching.Elapsed}.");
            window.Close();
        }, CancellationToken.None);
    }

}
