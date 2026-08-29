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
            workbench.ApplyViewport(800, 520);

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

            window.KeyPressQwerty(
                PhysicalKey.M,
                RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(WorkbenchDockIds.ProblemsTool, bottom.ActiveDockable?.Id);

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
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.ProblemsTool).Context),
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

            StatusIndicator[] toolStatuses = contexts
                .SelectMany(context => context.GetLogicalDescendants())
                .OfType<StatusIndicator>()
                .ToArray();
            Assert.True(toolStatuses.Length >= 6,
                $"Expected shared status indicators for core tools; found {toolStatuses.Length}.");
            Assert.All(toolStatuses, item => Assert.Equal(
                AutomationLiveSetting.Polite,
                AutomationProperties.GetLiveSetting(item)));

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
    public async Task Closed_conversation_panel_can_be_restored_and_activated()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(ApprovedGoalShell(), new());
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            IDockable conversation = Find<IDockable>(
                workbench.Root, WorkbenchDockIds.ConversationTool);
            IToolDock bottom = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Bottom);

            workbench.Factory.CloseDockable(conversation);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(bottom.VisibleDockables ?? [], item =>
                item.Id == WorkbenchDockIds.ConversationTool);

            Assert.True(workbench.ShowConversation());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(bottom.VisibleDockables ?? [], item =>
                item.Id == WorkbenchDockIds.ConversationTool);
            Assert.Equal(WorkbenchDockIds.ConversationTool, bottom.ActiveDockable?.Id);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Conversation_moved_to_document_region_survives_layout_restart()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            LayoutService layouts = new();
            WorkbenchDockHost first = CreateWorkbench(
                shell,
                layouts,
                conversation: ConversationSurface("First conversation"));
            Window firstWindow = new() { Width = 1280, Height = 800, Content = first.Control };
            firstWindow.Show();
            IDockable conversation = Find<IDockable>(
                first.Root, WorkbenchDockIds.ConversationTool);
            IToolDock bottom = Find<IToolDock>(first.Root, WorkbenchDockIds.Bottom);
            bottom.VisibleDockables!.Remove(conversation);
            first.Factory.AddDockable(first.Documents, conversation);
            first.Factory.SetActiveDockable(conversation);
            first.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            firstWindow.Close();

            Control restoredConversationSurface = ConversationSurface("Restored conversation");
            WorkbenchDockHost restored = CreateWorkbench(
                shell,
                layouts,
                conversation: restoredConversationSurface);
            Window restoredWindow = new()
            {
                Width = 1280,
                Height = 800,
                Content = restored.Control,
            };
            restoredWindow.Show();
            restored.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            IDockable restoredConversation = Find<IDockable>(
                restored.Root, WorkbenchDockIds.ConversationTool);
            Assert.Same(restored.Documents, restoredConversation.Owner);
            Assert.Contains(restored.Documents.VisibleDockables ?? [], item =>
                item.Id == WorkbenchDockIds.ConversationTool);
            Assert.Equal(
                WorkbenchDockIds.ConversationTool,
                restored.Documents.ActiveDockable?.Id);
            Assert.Contains(
                restoredConversationSurface,
                restoredWindow.GetVisualDescendants());
            restoredWindow.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Accessibility_tree_neutralizes_only_visual_implementation_containers()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(ApprovedGoalShell(), new());
            Window window = new() { Content = workbench.Control };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Control[] implementationContainers = window.GetVisualDescendants()
                .OfType<Control>()
                .Where(item => string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)))
                .Where(item =>
                {
                    AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(item);
                    return !peer.IsControlElement() && !peer.IsContentElement();
                })
                .ToArray();
            Assert.NotEmpty(implementationContainers);
            Button semanticButton = Assert.Single(
                Assert.IsType<StackPanel>(workbench.LayoutActions).Children.OfType<Button>(),
                item => AutomationProperties.GetName(item) == "Save current panel layout");

            AccessibilityTreeSemantics.Apply(window);

            Assert.All(implementationContainers, item =>
            {
                Assert.Equal("\u2063", AutomationProperties.GetName(item));
                Assert.Equal(string.Empty, AutomationProperties.GetClassNameOverride(item));
                Assert.Equal(
                    AutomationControlType.Custom,
                    AutomationProperties.GetControlTypeOverride(item));
            });
            Assert.Equal("Save current panel layout", AutomationProperties.GetName(semanticButton));
            Assert.Null(AutomationProperties.GetClassNameOverride(semanticButton));
            Assert.Null(AutomationProperties.GetControlTypeOverride(semanticButton));
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
    public async Task Workspace_switch_is_blocked_until_dirty_documents_are_resolved()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true },
                prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved workspace-specific content";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            bool cancelled = workbench.PrepareForWorkspaceChangeAsync()
                .AsTask().GetAwaiter().GetResult();

            Assert.False(cancelled);
            Assert.Equal(1, workbench.SourceDocumentCount);
            Assert.Equal("unsaved workspace-specific content", workbench.ActiveSourceEditor?.Text);
            Assert.Equal(WorkbenchDocumentTransition.Switch,
                Assert.Single(prompt.UnsavedPrompts).Transition);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            bool accepted = workbench.PrepareForWorkspaceChangeAsync()
                .AsTask().GetAwaiter().GetResult();
            Assert.True(accepted);
            Assert.Equal(0, workbench.SourceDocumentCount);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Layout_reset_keeps_durable_controls_in_the_rendered_tree()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = AvaloniaShellState.Initial with { IsLoading = false };
            TextBlock navigation = new() { Text = "Rendered workspace navigation" };
            TextBlock conversation = new() { Text = "Rendered conversation" };
            TextBlock context = new() { Text = "Rendered goal context" };
            WorkbenchDockHost workbench = new(
                new RunOutputService(),
                new InspectionService(),
                new DocumentService(),
                new CodeIntelligenceService(),
                new LayoutService(),
                new DocumentPrompt(),
                () => shell,
                navigation,
                conversation,
                context,
                CancellationToken.None);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(workbench.OverviewAction, window.GetVisualDescendants());
            Assert.Contains(navigation, window.GetVisualDescendants());

            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(workbench.OverviewAction, window.GetVisualDescendants());
            Assert.Contains(navigation, window.GetVisualDescendants());
            Assert.Contains(conversation, window.GetVisualDescendants());
            Assert.Contains(context, window.GetVisualDescendants());
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
            Assert.Contains(Assert.IsType<StackPanel>(first.DocumentActions).Children, item =>
                AutomationProperties.GetName(item) == "Workbench layout status");
            Assert.Contains(layoutActions.Children, item =>
                AutomationProperties.GetName(item) == "Save current panel layout");
            Assert.Contains(layoutActions.Children, item =>
                AutomationProperties.GetName(item) == "Reset panels to the default layout");
            first.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            IToolDock left = Find<IToolDock>(first.Root, WorkbenchDockIds.Left);
            IToolDock right = Find<IToolDock>(first.Root, WorkbenchDockIds.Right);
            IToolDock bottom = Find<IToolDock>(first.Root, WorkbenchDockIds.Bottom);
            IDockable navigation = Find<IDockable>(first.Root, WorkbenchDockIds.NavigationTool);
            IDockable files = Find<IDockable>(first.Root, WorkbenchDockIds.FilesTool);
            IDockable git = Find<IDockable>(first.Root, WorkbenchDockIds.GitTool);
            IDockable conversation = Find<IDockable>(
                first.Root, WorkbenchDockIds.ConversationTool);
            left.VisibleDockables!.Remove(navigation);
            right.VisibleDockables!.Add(navigation);
            left.VisibleDockables.Remove(files);
            first.Root.HiddenDockables ??= first.Factory.CreateList<IDockable>();
            first.Root.HiddenDockables.Add(files);
            bottom.VisibleDockables!.Remove(conversation);
            first.Root.HiddenDockables.Add(conversation);
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
                item.Id == WorkbenchDockIds.NavigationTool && item.Context is TabControl tabs &&
                tabs.Items.Count == 4);
            Assert.Contains(restored.Root.HiddenDockables!, item =>
                item.Id == WorkbenchDockIds.FilesTool && item.Context is not null);
            Assert.Contains(restored.Root.HiddenDockables!, item =>
                item.Id == WorkbenchDockIds.ConversationTool && item.Context is not null);
            Assert.True(restored.ShowConversation());
            IToolDock restoredBottom = Find<IToolDock>(restored.Root, WorkbenchDockIds.Bottom);
            Assert.Contains(restoredBottom.VisibleDockables!, item =>
                item.Id == WorkbenchDockIds.ConversationTool);
            Assert.Equal(WorkbenchDockIds.ConversationTool, restoredBottom.ActiveDockable?.Id);
            Assert.Equal(0.5, restoredLeft.Proportion);
            Assert.Equal(0.37, restoredRight.Proportion);
            Assert.Single(restored.Documents.VisibleDockables!);
            IDockWindow restoredWindowState = Assert.Single(restored.Root.Windows!);
            Assert.Equal(0, restoredWindowState.X);
            Assert.Equal(0, restoredWindowState.Y);
            Assert.Equal(1920, restoredWindowState.Width);
            Assert.Equal(1280, restoredWindowState.Height);
            Assert.Equal(7, DurableTools(restored.Root).Count);
            Assert.Equal("Layout restored", restored.LayoutStatusText);
            restoredWindow.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Legacy_six_tool_layout_is_upgraded_with_the_problems_pane()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            LayoutService layouts = new();
            WorkbenchDockHost source = CreateWorkbench(shell, layouts);
            source.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            JsonNode payload = JsonNode.Parse(layouts.Stored!)!;
            RemovePane(payload, WorkbenchDockIds.ProblemsTool);
            layouts.Stored = payload.ToJsonString();

            WorkbenchDockHost restored = CreateWorkbench(shell, layouts);
            restored.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();

            Assert.NotNull(Find<ITool>(restored.Root, WorkbenchDockIds.ProblemsTool));
            Assert.Equal(7, DurableTools(restored.Root).Count);
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
            Assert.Equal(7, DurableTools(unknown.Root).Count);

            layouts.Stored = validLayout.Replace("\"Version\": 2", "\"Version\": 1",
                StringComparison.Ordinal);
            WorkbenchDockHost obsolete = CreateWorkbench(shell, layouts);
            obsolete.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Contains("rejected", obsolete.LayoutStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(7, DurableTools(obsolete.Root).Count);

            layouts.Stored = validLayout.Replace(
                WorkbenchDockIds.FilesTool,
                WorkbenchDockIds.NavigationTool,
                StringComparison.Ordinal);

            WorkbenchDockHost workbench = CreateWorkbench(shell, layouts);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();

            Assert.Contains("rejected", workbench.LayoutStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(7, DurableTools(workbench.Root).Count);

            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.True(layouts.WasReset);
            Assert.Null(layouts.Stored);
            Assert.Equal("Default layout restored", workbench.LayoutStatusText);
            Assert.Equal(2, Find<IToolDock>(workbench.Root, WorkbenchDockIds.Left)
                .VisibleDockables?.Count);
            window.Close();
        }, CancellationToken.None);
    }

}
