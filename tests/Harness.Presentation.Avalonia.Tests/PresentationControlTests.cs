using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed class PresentationControlTests
{
    [Fact]
    public void Closing_a_document_decision_dialog_defaults_to_cancel()
    {
        Assert.Equal(WorkbenchUnsavedDecision.Cancel, default(WorkbenchUnsavedDecision));
        Assert.Equal(WorkbenchConflictDecision.Cancel, default(WorkbenchConflictDecision));
    }

    [Fact]
    public async Task Markdown_content_renders_without_raw_provider_markup()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = MarkdownContentView.Create(
                "# Answer\n\nI am **Gemma 4** 😊</blockquote>\n\n```csharp\nvar answer = 4;\n```",
                _ => Brushes.Transparent);
            Window window = new() { Content = content };
            window.Show();

            string rendered = string.Join('\n', window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text));
            Assert.Contains("Gemma 4", rendered, StringComparison.Ordinal);
            Assert.Contains("var answer = 4;", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("**", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("blockquote", rendered, StringComparison.OrdinalIgnoreCase);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Code_editor_loads_with_required_style_and_real_text()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            TextEditor editor = CodeEditorView.Create("diff --git a/App.cs b/App.cs");
            Window window = new() { Content = editor };
            window.Show();

            Assert.Equal("diff --git a/App.cs b/App.cs", editor.Text);
            Assert.True(editor.IsReadOnly);
            Assert.True(editor.ShowLineNumbers);
            Assert.NotNull(editor.Template);
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
            WorkbenchDockHost workbench = new(
                new RunOutputService(),
                new InspectionService(),
                new DocumentService(),
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
            Assert.Equal(6, DurableTools(workbench.Root).Count);
            Control documentContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            TextEditor editor = Assert.Single(
                documentContent.GetVisualDescendants().OfType<TextEditor>());
            Assert.Contains(editor, window.GetVisualDescendants().OfType<TextEditor>());
            Assert.Equal("namespace Example;", editor.Text);
            Assert.True(editor.IsReadOnly);
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
            Assert.Equal(["Save", "Reload", "Close"],
                documentActions.Select(item => item.Content?.ToString() ?? string.Empty).ToArray());
            Assert.All(documentActions, item => Assert.False(
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(item))));
            Assert.True(documentActions[0].IsEnabled);
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.S,
                KeyModifiers = KeyModifiers.Control,
            });

            WorkbenchDocumentSaveRequest request = Assert.Single(documents.SaveRequests);
            Assert.Equal("goal-1", request.GoalId.Value);
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
            Assert.Equal("first diff", Assert.IsType<TextEditor>(diff.Context).Text);
            Assert.All(inspection.Requests, request => Assert.Equal("goal-1", request.GoalId?.Value));

            inspection.Diff = "refreshed diff";
            workbench.OpenDiffAsync().AsTask().GetAwaiter().GetResult();
            Assert.Same(diff, workbench.Documents.ActiveDockable);
            Assert.Equal("refreshed diff", Assert.IsType<TextEditor>(diff.Context).Text);

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

    [Fact]
    public async Task Run_output_tool_renders_only_typed_durable_execution_evidence()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RunOutputService output = new()
            {
                Result = new(
                    [new(
                        new("run-1"),
                        new("goal-1"),
                        new("build-1"),
                        DotNetOperation.Build,
                        ToolEvidenceState.Failed,
                        new(
                            "goal-1",
                            new("build-1"),
                            DotNetOperation.Build,
                            "Harness.slnx",
                            1,
                            "compiler output",
                            "CS1002: ; expected",
                            IsOutputTruncated: true,
                            IsErrorTruncated: false,
                            WasCancelled: false,
                            DurationMilliseconds: 725,
                            "process_failed",
                            "Build failed."),
                        now,
                        now.AddMilliseconds(725),
                        Error: null)],
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                runOutput: output);
            Window window = new() { Content = workbench.Control };
            window.Show();

            workbench.RefreshRunOutputAsync().AsTask().GetAwaiter().GetResult();

            ITool tool = Find<ITool>(workbench.Root, WorkbenchDockIds.RunOutputTool);
            Control content = Assert.IsAssignableFrom<Control>(tool.Context);
            ListBox runs = Assert.Single(content.GetVisualDescendants().OfType<ListBox>());
            TextEditor details = Assert.Single(content.GetVisualDescendants().OfType<TextEditor>());
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(runs.ItemsSource));
            Assert.Contains("Build · Failed", details.Text, StringComparison.Ordinal);
            Assert.Contains("Harness.slnx", details.Text, StringComparison.Ordinal);
            Assert.Contains("Standard output · truncated", details.Text, StringComparison.Ordinal);
            Assert.Contains("compiler output", details.Text, StringComparison.Ordinal);
            Assert.Contains("CS1002: ; expected", details.Text, StringComparison.Ordinal);
            Assert.Equal("goal-1", Assert.Single(output.Requests).Value);

            output.Result = new([], false, null, null);
            workbench.RefreshRunOutputAsync().AsTask().GetAwaiter().GetResult();
            TextBlock status = Assert.Single(content.GetVisualDescendants().OfType<TextBlock>(),
                item => AutomationProperties.GetName(item) == "Durable run output status");
            Assert.Contains("No Build, Test, or Restore runs", status.Text, StringComparison.Ordinal);
            Assert.Equal(string.Empty, details.Text);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Compact_viewport_collapses_tools_and_keyboard_commands_restore_access()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(ApprovedGoalShell(), new());
            Window window = new() { Width = 800, Height = 600, Content = workbench.Control };
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            workbench.ApplyViewport(800, 600);

            IToolDock left = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Left);
            IToolDock right = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Right);
            IToolDock bottom = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Bottom);
            Assert.True(workbench.IsCompactViewport);
            Assert.False(left.IsExpanded);
            Assert.False(right.IsExpanded);
            Assert.False(bottom.IsExpanded);
            Assert.True(left.MaxWidth <= 76);
            Assert.True(right.MaxWidth <= 76);
            Assert.True(bottom.MaxHeight <= 84);
            Assert.All(left.VisibleDockables!, item =>
                Assert.False(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));
            Assert.All(right.VisibleDockables!, item =>
                Assert.False(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));
            Assert.All(bottom.VisibleDockables!, item =>
                Assert.False(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));

            Assert.True(workbench.Control.Focus());
            window.KeyPressQwerty(
                PhysicalKey.G,
                RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(right.IsExpanded);
            Assert.Equal(WorkbenchDockIds.GitTool, right.ActiveDockable?.Id);
            Assert.All(right.VisibleDockables!, item =>
                Assert.True(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));

            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            Assert.True(bottom.IsExpanded);
            Assert.Equal(WorkbenchDockIds.RunOutputTool, bottom.ActiveDockable?.Id);
            Assert.All(bottom.VisibleDockables!, item =>
                Assert.True(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));

            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(left.IsExpanded);
            Assert.Equal(WorkbenchDockIds.FilesTool, left.ActiveDockable?.Id);
            Assert.All(left.VisibleDockables!, item =>
                Assert.True(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));
            Assert.True(workbench.LastRequestedFocusTarget?.Focusable);

            workbench.ApplyViewport(1280, 800);
            Assert.False(workbench.IsCompactViewport);
            Assert.True(left.IsExpanded);
            Assert.True(right.IsExpanded);
            Assert.True(bottom.IsExpanded);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Floating_tools_use_the_originating_dock_window_as_owner()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(TrustedShell(), new());
            Window owner = new() { Content = workbench.Control };
            owner.Show();
            IDockable git = Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool);

            workbench.Factory.FloatDockable(git);

            IDockWindow floating = Assert.Single(workbench.Root.Windows!);
            Assert.Equal(DockWindowOwnerMode.DockableWindow, floating.OwnerMode);
            Assert.False(floating.ShowInTaskbar);
            IToolDock floatingDock = Assert.IsAssignableFrom<IToolDock>(floating.Layout?.ActiveDockable);
            Assert.Same(git, floatingDock.ActiveDockable);
            owner.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workbench_renders_at_two_hundred_percent_without_changing_logical_layout()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(ApprovedGoalShell(), new());
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Size logicalSize = window.ClientSize;
            using Bitmap normal = Assert.IsAssignableFrom<Bitmap>(
                window.CaptureRenderedFrame());

            window.SetRenderScaling(2.0);
            Dispatcher.UIThread.RunJobs();
            using Bitmap highDpi = Assert.IsAssignableFrom<Bitmap>(
                window.CaptureRenderedFrame());

            Assert.Equal(logicalSize, window.ClientSize);
            Assert.Equal(normal.PixelSize.Width * 2, highDpi.PixelSize.Width);
            Assert.Equal(normal.PixelSize.Height * 2, highDpi.PixelSize.Height);
            Assert.Equal(logicalSize.Width, workbench.Control.Bounds.Width);
            Assert.Equal(logicalSize.Height, workbench.Control.Bounds.Height);
            IToolDock left = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Left);
            IToolDock right = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Right);
            IToolDock bottom = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Bottom);
            Assert.True(left.IsExpanded);
            Assert.True(right.IsExpanded);
            Assert.True(bottom.IsExpanded);
            Assert.False(left.IsEmpty);
            Assert.False(right.IsEmpty);
            Assert.False(bottom.IsEmpty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workbench_actions_and_editors_have_explicit_accessible_names()
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
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Control[] contexts =
            [
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.FilesTool).Context),
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context),
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.ContextTool).Context),
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.RunOutputTool).Context),
                workbench.LayoutActions,
                Assert.IsAssignableFrom<Control>(workbench.Documents.ActiveDockable?.Context),
            ];
            Control[] interactive = contexts
                .SelectMany(context => context.GetVisualDescendants().OfType<Control>())
                .Where(item => item is Button or TextBox or ListBox or TextEditor)
                .ToArray();

            Assert.NotEmpty(interactive);
            Assert.All(interactive, item => Assert.False(
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)),
                $"{item.GetType().Name} has no explicit accessible name."));

            Button[] chromeButtons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(item => item.Name is "PART_MenuButton" or
                                           "PART_PinButton" or
                                           "PART_MaximizeRestoreButton" or
                                           "PART_CloseButton")
                .ToArray();
            Assert.NotEmpty(chromeButtons);
            Assert.All(chromeButtons, item => Assert.DoesNotContain(
                "Viewbox",
                AutomationProperties.GetName(item) ?? string.Empty,
                StringComparison.Ordinal));
            Assert.All(chromeButtons, item => Assert.False(
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)),
                $"Dock chrome button {item.Name} has no accessible name."));

            ToolChromeControl[] chrome = window.GetVisualDescendants()
                .OfType<ToolChromeControl>()
                .ToArray();
            Assert.NotEmpty(chrome);
            Assert.All(chrome, item => Assert.EndsWith(
                " panel controls",
                AutomationProperties.GetName(item),
                StringComparison.Ordinal));

            Control[] splitters = window.GetVisualDescendants()
                .OfType<Control>()
                .Where(item => item.GetType().Name == "ProportionalStackPanelSplitter")
                .ToArray();
            Assert.NotEmpty(splitters);
            Assert.All(splitters, item => Assert.Equal(
                "Resize adjacent workbench panels",
                AutomationProperties.GetName(item)));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Layout_reset_cannot_drop_a_dirty_source_buffer()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            LayoutService layouts = new();
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                shell,
                layouts,
                new() { Editable = true },
                prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal(1, workbench.SourceDocumentCount);
            Assert.False(layouts.WasReset);
            Assert.Contains("cancelled", workbench.LayoutStatusText, StringComparison.OrdinalIgnoreCase);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal(0, workbench.SourceDocumentCount);
            Assert.True(layouts.WasReset);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workbench_layout_round_trips_moved_hidden_and_floating_production_panels()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            LayoutService layouts = new();
            WorkbenchDockHost first = CreateWorkbench(shell, layouts);
            Window firstWindow = new() { Content = first.Control };
            firstWindow.Show();
            StackPanel layoutActions = Assert.IsType<StackPanel>(first.LayoutActions);
            Assert.Contains(layoutActions.Children, item =>
                AutomationProperties.GetName(item) == "Workbench layout status");
            Assert.Contains(layoutActions.Children, item =>
                AutomationProperties.GetName(item) == "Save current panel layout");
            Assert.Contains(layoutActions.Children, item =>
                AutomationProperties.GetName(item) == "Reset panels to the default layout");
            first.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            IToolDock left = Find<IToolDock>(first.Root, WorkbenchDockIds.Left);
            IToolDock right = Find<IToolDock>(first.Root, WorkbenchDockIds.Right);
            IDockable navigation = Find<IDockable>(first.Root, WorkbenchDockIds.NavigationTool);
            IDockable files = Find<IDockable>(first.Root, WorkbenchDockIds.FilesTool);
            IDockable git = Find<IDockable>(first.Root, WorkbenchDockIds.GitTool);
            left.VisibleDockables!.Remove(navigation);
            right.VisibleDockables!.Add(navigation);
            left.VisibleDockables.Remove(files);
            first.Root.HiddenDockables ??= first.Factory.CreateList<IDockable>();
            first.Root.HiddenDockables.Add(files);
            right.VisibleDockables.Remove(git);
            IToolDock floatingTools = first.Factory.CreateToolDock();
            floatingTools.Id = "dock.floating.git";
            floatingTools.VisibleDockables = first.Factory.CreateList(git);
            IRootDock floatingRoot = first.Factory.CreateRootDock();
            floatingRoot.Id = "dock.floating.root";
            floatingRoot.VisibleDockables = first.Factory.CreateList<IDockable>(floatingTools);
            IDockWindow floating = first.Factory.CreateDockWindow();
            floating.Id = "window.git";
            floating.X = 5000;
            floating.Y = -5000;
            floating.Width = 5000;
            floating.Height = 5000;
            floating.Layout = floatingRoot;
            first.Root.Windows = first.Factory.CreateList(floating);
            left.Proportion = double.NaN;
            right.Proportion = 0.37;

            first.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.NotNull(layouts.Stored);
            Assert.DoesNotContain("document.file", layouts.Stored, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace Example", layouts.Stored, StringComparison.Ordinal);
            firstWindow.Close();

            WorkbenchDockHost restored = CreateWorkbench(shell, layouts);
            Window restoredWindow = new() { Content = restored.Control };
            restoredWindow.Show();
            restored.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();

            IToolDock restoredRight = Find<IToolDock>(restored.Root, WorkbenchDockIds.Right);
            IToolDock restoredLeft = Find<IToolDock>(restored.Root, WorkbenchDockIds.Left);
            Assert.Contains(restoredRight.VisibleDockables!, item =>
                item.Id == WorkbenchDockIds.NavigationTool && item.Context is TextBlock);
            Assert.Contains(restored.Root.HiddenDockables!, item =>
                item.Id == WorkbenchDockIds.FilesTool && item.Context is not null);
            Assert.Equal(0.5, restoredLeft.Proportion);
            Assert.Equal(0.37, restoredRight.Proportion);
            Assert.Single(restored.Documents.VisibleDockables!);
            IDockWindow restoredWindowState = Assert.Single(restored.Root.Windows!);
            Assert.Equal(0, restoredWindowState.X);
            Assert.Equal(0, restoredWindowState.Y);
            Assert.Equal(1920, restoredWindowState.Width);
            Assert.Equal(1280, restoredWindowState.Height);
            Assert.Equal(6, DurableTools(restored.Root).Count);
            Assert.Equal("Layout restored", restored.LayoutStatusText);
            restoredWindow.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workbench_rejects_unknown_and_duplicate_layout_and_reset_restores_known_default()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            LayoutService layouts = new();
            WorkbenchDockHost source = CreateWorkbench(shell, layouts);
            source.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            string validLayout = layouts.Stored!;
            layouts.Stored = validLayout.Replace(
                WorkbenchDockIds.FilesTool,
                "tool.unknown",
                StringComparison.Ordinal);

            WorkbenchDockHost unknown = CreateWorkbench(shell, layouts);
            unknown.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Contains("rejected", unknown.LayoutStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(6, DurableTools(unknown.Root).Count);

            layouts.Stored = validLayout.Replace("\"Version\": 2", "\"Version\": 1",
                StringComparison.Ordinal);
            WorkbenchDockHost obsolete = CreateWorkbench(shell, layouts);
            obsolete.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Contains("rejected", obsolete.LayoutStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(6, DurableTools(obsolete.Root).Count);

            layouts.Stored = validLayout.Replace(
                WorkbenchDockIds.FilesTool,
                WorkbenchDockIds.NavigationTool,
                StringComparison.Ordinal);

            WorkbenchDockHost workbench = CreateWorkbench(shell, layouts);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();

            Assert.Contains("rejected", workbench.LayoutStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(6, DurableTools(workbench.Root).Count);

            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.True(layouts.WasReset);
            Assert.Null(layouts.Stored);
            Assert.Equal("Default layout restored", workbench.LayoutStatusText);
            Assert.Equal(2, Find<IToolDock>(workbench.Root, WorkbenchDockIds.Left)
                .VisibleDockables?.Count);
            window.Close();
        }, CancellationToken.None);
    }

    private static WorkbenchDockHost CreateWorkbench(
        AvaloniaShellState shell,
        LayoutService layouts,
        DocumentService? documents = null,
        DocumentPrompt? prompt = null,
        InspectionService? inspection = null,
        RunOutputService? runOutput = null) => new(
        runOutput ?? new RunOutputService(),
        inspection ?? new InspectionService(),
        documents ?? new DocumentService(),
        layouts,
        prompt ?? new DocumentPrompt(),
        () => shell,
        new TextBlock { Text = "Workspace" },
        new TextBlock { Text = "Conversation" },
        new TextBlock { Text = "Goal context" },
        CancellationToken.None);

    private static AvaloniaShellState TrustedShell()
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
        return AvaloniaShellState.Initial with
        {
            Workspaces = WorkspaceManagementState.Initial with { Registered = [workspace] },
            IsLoading = false,
        };
    }

    private static AvaloniaShellState ApprovedGoalShell()
    {
        AvaloniaShellState shell = TrustedShell();
        GoalView goal = new(
            new("goal-1"),
            "workspace-1",
            "Edit source safely",
            "Change source only in the isolated worktree.",
            new(2),
            RemoteBudget: null,
            GoalState.Approved,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return shell with
        {
            Goals = GoalManagementState.Initial with
            {
                Items = [goal],
                SelectedGoalId = goal.Id,
            },
        };
    }

    private static T Find<T>(IDockable root, string id)
        where T : class, IDockable
    {
        HashSet<IDockable> visited = new(ReferenceEqualityComparer.Instance);
        Stack<IDockable> pending = new();
        pending.Push(root);
        while (pending.TryPop(out IDockable? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current.Id == id)
            {
                return Assert.IsAssignableFrom<T>(current);
            }

            if (current is IDock dock)
            {
                foreach (IDockable child in dock.VisibleDockables ?? [])
                {
                    pending.Push(child);
                }
            }

            if (current is IRootDock rootDock)
            {
                foreach (IDockable child in (rootDock.HiddenDockables ?? [])
                             .Concat(rootDock.LeftPinnedDockables ?? [])
                             .Concat(rootDock.RightPinnedDockables ?? [])
                             .Concat(rootDock.TopPinnedDockables ?? [])
                             .Concat(rootDock.BottomPinnedDockables ?? []))
                {
                    pending.Push(child);
                }

                foreach (IDockWindow window in rootDock.Windows ?? [])
                {
                    if (window.Layout is not null)
                    {
                        pending.Push(window.Layout);
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Dockable '{id}' was not found.");
    }

    private static IReadOnlyList<ITool> DurableTools(IRootDock root) =>
        WorkbenchDockIds.DurablePaneIds
            .Where(id => id.StartsWith("tool.", StringComparison.Ordinal))
            .Select(id => Find<ITool>(root, id))
            .ToArray();

    private sealed class InspectionService : IWorkbenchInspectionService
    {
        internal List<WorkbenchWorkspaceRequest> Requests { get; } = [];
        internal string Diff { get; set; } = "first diff";

        public ValueTask<WorkbenchTextSearchResult> SearchTextAsync(
            WorkbenchWorkspaceRequest request,
            string query,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkbenchTextSearchResult(
                Context(request),
                new(
                    [new("src/App.cs", 1, "namespace Example;")],
                    1,
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null)));
        }

        public ValueTask<WorkbenchGitInspectionResult> InspectGitAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            WorkbenchWorkspaceContext context = Context(request);
            return ValueTask.FromResult(new WorkbenchGitInspectionResult(
                context,
                new(
                    context.Branch?.Value ?? "main",
                    "abc123",
                    [new("src/App.cs", "modified")],
                    Diff,
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null)));
        }

        private static WorkbenchWorkspaceContext Context(WorkbenchWorkspaceRequest request) =>
            request.GoalId is null
                ? new(
                    request.WorkspaceId,
                    null,
                    new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace")
                : new(
                    request.WorkspaceId,
                    request.GoalId,
                    new("harness/goal-1"),
                    WorkbenchWorkspaceScope.ApprovedGoalWorktree,
                    "Approved goal worktree · harness/goal-1");
    }

    private sealed class RunOutputService : IRunOutputService
    {
        internal RunOutputSnapshot Result { get; set; } = new([], false, null, null);
        internal List<GoalId> Requests { get; } = [];

        public ValueTask<RunOutputSnapshot> ListAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(goalId);
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class DocumentService : IWorkbenchDocumentService
    {
        internal bool Editable { get; init; }
        internal string Content { get; set; } = "namespace Example;";
        internal List<WorkbenchDocumentSaveRequest> SaveRequests { get; } = [];
        internal Queue<WorkbenchDocumentSaveResult> SaveResults { get; } = [];

        public ValueTask<WorkbenchDocumentView> OpenAsync(
            WorkbenchDocumentOpenRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkbenchDocumentView(
                request.WorkspaceId,
                Editable ? request.GoalId : null,
                Editable ? new("harness/goal-1") : null,
                request.Path,
                new(Content),
                new("7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4"),
                new(Content.Length),
                IsTruncated: false,
                Editable ? WorkbenchDocumentAccess.Editable : WorkbenchDocumentAccess.ReadOnly,
                Editable ? "Editing isolated branch harness/goal-1." : "Read-only original workspace.",
                ErrorCode: null,
                Error: null));

        public ValueTask<WorkbenchDocumentSaveResult> SaveAsync(
            WorkbenchDocumentSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveRequests.Add(request);
            return ValueTask.FromResult(SaveResults.TryDequeue(out WorkbenchDocumentSaveResult? result)
                ? result
                : new WorkbenchDocumentSaveResult(
                    request.GoalId,
                    request.CorrelationId,
                    request.Path,
                    request.ExpectedSha256,
                    request.ExpectedSha256,
                    new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                    new(request.Content.Value.Length),
                    WorkbenchDocumentSaveOutcome.Saved,
                    ErrorCode: null,
                    Error: null));
        }
    }

    private sealed class DocumentPrompt : IWorkbenchDocumentPrompt
    {
        internal Queue<WorkbenchUnsavedDecision> UnsavedDecisions { get; } = [];
        internal Queue<WorkbenchConflictDecision> ConflictDecisions { get; } = [];
        internal List<WorkbenchUnsavedPrompt> UnsavedPrompts { get; } = [];
        internal List<WorkbenchConflictPrompt> ConflictPrompts { get; } = [];

        public ValueTask<WorkbenchUnsavedDecision> DecideUnsavedAsync(
            WorkbenchUnsavedPrompt prompt,
            Window? owner)
        {
            UnsavedPrompts.Add(prompt);
            return ValueTask.FromResult(UnsavedDecisions.TryDequeue(out WorkbenchUnsavedDecision decision)
                ? decision
                : WorkbenchUnsavedDecision.Cancel);
        }

        public ValueTask<WorkbenchConflictDecision> DecideConflictAsync(
            WorkbenchConflictPrompt prompt,
            Window? owner)
        {
            ConflictPrompts.Add(prompt);
            return ValueTask.FromResult(ConflictDecisions.TryDequeue(out WorkbenchConflictDecision decision)
                ? decision
                : WorkbenchConflictDecision.Cancel);
        }
    }

    private sealed class LayoutService : IWorkbenchLayoutService
    {
        internal string? Stored { get; set; }
        internal bool WasReset { get; private set; }

        public ValueTask<WorkbenchLayoutLoadResult> LoadAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            Stored is null
                ? new WorkbenchLayoutLoadResult(WorkbenchLayoutLoadState.Missing, null, null)
                : new WorkbenchLayoutLoadResult(
                    WorkbenchLayoutLoadState.Available,
                    new(Stored),
                    null));

        public ValueTask<WorkbenchLayoutWriteResult> SaveAsync(
            WorkbenchLayoutPayload layout,
            CancellationToken cancellationToken = default)
        {
            Stored = layout.Value;
            return ValueTask.FromResult(new WorkbenchLayoutWriteResult(true, null));
        }

        public ValueTask<WorkbenchLayoutWriteResult> ResetAsync(
            CancellationToken cancellationToken = default)
        {
            Stored = null;
            WasReset = true;
            return ValueTask.FromResult(new WorkbenchLayoutWriteResult(true, null));
        }
    }
}
