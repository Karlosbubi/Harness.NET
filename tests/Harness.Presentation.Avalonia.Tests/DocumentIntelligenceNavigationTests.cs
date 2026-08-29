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
    public async Task Source_editor_exposes_semantic_outline_and_clickable_breadcrumbs()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Presentation = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(new(new(0, 0), new(0, 9)),
                        WorkbenchCodeClassificationKind.Keyword)],
                    [],
                    [new(WorkbenchCodeSymbolKind.Namespace, new("Example"),
                        new(new(0, 0), new(0, 18)),
                        new(new(0, 10), new(0, 17)), 0)],
                    [new(WorkbenchCodeSymbolKind.Namespace, new("Example"),
                        new(new(0, 10), new(0, 17)))],
                    [],
                    [],
                    false,
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                if (workbench.Documents.ActiveDockable?.Context is Control current &&
                    current.GetVisualDescendants().OfType<Button>().Any(button =>
                        Equals(button.Content, "Example")))
                {
                    break;
                }
                Thread.Sleep(10);
            }

            Control source = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Button outline = Assert.Single(source.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Outline"));
            Button breadcrumb = Assert.Single(source.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Example"));
            Assert.True(outline.IsEnabled);
            Assert.Equal("Go to Example", AutomationProperties.GetName(breadcrumb));

            breadcrumb.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(11, workbench.ActiveSourceEditor?.TextArea.Caret.Column);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_opens_accessible_roslyn_completion_and_quick_info_from_keyboard()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Completions = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    new("list-1"),
                    new(request.Snapshot.Position, request.Snapshot.Position),
                    [new(
                        new("item-1"),
                        new("Example"),
                        new("Example"),
                        new("Example"),
                        new("namespace"),
                        WorkbenchCodeSymbolKind.Namespace,
                        ['\t', '\n', '('],
                        IsRecommended: false)],
                    []),
                CompletionCommit = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(
                        new(request.Snapshot.Position, request.Snapshot.Position),
                        new("Example"))],
                    new(
                        request.Snapshot.Position.Line,
                        request.Snapshot.Position.Character + "Example".Length),
                    []),
                QuickInfo = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    new(snapshot.Position, snapshot.Position),
                    [new("namespace Example")],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            editor.CaretOffset = editor.Text.Length;

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.Control,
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, workbench.ActiveCompletionItemCount);
            CompletionWindow completionWindow = workbench.ActiveCompletionWindow!;
            completionWindow.CompletionList.CompletionData[0].Complete(
                editor.TextArea,
                new SimpleSegment(editor.CaretOffset, 0),
                EventArgs.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.EndsWith("Example", editor.Text, StringComparison.Ordinal);
            RoslynCompletionData completion = new(
                codeIntelligence.Completions(new(
                    new(
                        new("session-1"),
                        new("src/App.cs"),
                        new("7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4"),
                        new(1),
                        new(editor.Text),
                        new(0, 0)),
                    WorkbenchCodeCompletionTriggerKind.Invoke,
                    null)).Items[0],
                (_, _) => { });
            Assert.Contains(
                "Namespace Example namespace",
                AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(completion.Content)),
                StringComparison.Ordinal);
            char? committedWith = null;
            RoslynCompletionData commitData = new(
                codeIntelligence.Completions(new(
                    new(
                        new("session-1"),
                        new("src/App.cs"),
                        new("7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4"),
                        new(1),
                        new(editor.Text),
                        new(0, 0)),
                    WorkbenchCodeCompletionTriggerKind.Invoke,
                    null)).Items[0],
                (_, character) => committedWith = character);
            commitData.CompleteWithCharacter('(');
            Assert.Equal('(', committedWith);
            RoslynOverloadProvider overloads = new(new(
                new("session-1"),
                new("src/App.cs"),
                new(1),
                WorkbenchCodeResultState.Ready,
                [new(
                    new("void Run(string text, int count)"),
                    new("Runs the operation."),
                    [new(new("text"), new("string text"), new(string.Empty)),
                     new(new("count"), new("int count"), new(string.Empty))])],
                0,
                1,
                []));
            Assert.Contains(
                "parameter 2",
                AutomationProperties.GetName(
                    Assert.IsAssignableFrom<Control>(overloads.CurrentHeader)),
                StringComparison.OrdinalIgnoreCase);

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.K,
                KeyModifiers = KeyModifiers.Control,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.True(workbench.ActiveQuickInfoIsOpen);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_dispatches_the_saved_completion_keybinding_only()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Completions = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    new("custom-keys"),
                    new(request.Snapshot.Position, request.Snapshot.Position),
                    [new(new("item"), new("Example"), new("Example"), new("Example"),
                        new("namespace"), WorkbenchCodeSymbolKind.Namespace, ['\t'], false)],
                    []),
            };
            KeybindingSettingsSnapshot defaults = KeybindingSettingsSnapshot.Default;
            KeybindingSettingsSnapshot custom = defaults with
            {
                Bindings = defaults.Bindings.Select(binding =>
                    binding.Definition.Command is KeybindingCommand.ShowCompletion
                        ? binding with
                        {
                            Gestures = [new(
                                KeybindingModifiers.Control | KeybindingModifiers.Shift,
                                KeybindingKey.Q)],
                        }
                        : binding).ToArray(),
                UsesDefaults = false,
            };
            AvaloniaShellState shell = TrustedShell() with
            {
                Settings = TrustedShell().Settings with { KeybindingSettings = custom },
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                shell, new(), codeIntelligence: codeIntelligence);
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            Control source = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Button completion = Assert.Single(source.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "IntelliSense"));
            Assert.Contains("Ctrl+Shift+Q", Assert.IsType<string>(ToolTip.GetTip(completion)),
                StringComparison.Ordinal);

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.Control,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, workbench.ActiveCompletionItemCount);

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Q,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, workbench.ActiveCompletionItemCount);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_applies_vim_mode_to_the_live_editable_buffer_and_reports_mode()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            KeybindingSettingsSnapshot vim = KeybindingSettingsSnapshot.Default with
            {
                InputMode = EditorInputMode.Vim,
            };
            AvaloniaShellState shell = TrustedShell() with
            {
                Settings = TrustedShell().Settings with { KeybindingSettings = vim },
            };
            DocumentService documents = new() { Content = "one two\nthree\n" };
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents);
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            Control source = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);

            Assert.Contains("VIM NORMAL", string.Join('\n', source
                .GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.W,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, editor.CaretOffset);

            KeyEventArgs deleteKey = new()
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.X,
            };
            editor.RaiseEvent(deleteKey);
            Dispatcher.UIThread.RunJobs();
            Assert.True(deleteKey.Handled);
            Assert.Equal("one wo\nthree\n", editor.Text);

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.I,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("VIM INSERT", string.Join('\n', source
                .GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("VIM NORMAL", string.Join('\n', source
                .GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F12_definition_navigation_moves_to_the_exact_source_range()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Definition = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(
                        WorkbenchCodeDestinationKind.Source,
                        new("Example"),
                        snapshot.Path,
                        new(new(0, 2), new(0, 9)))],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F12,
                KeyModifiers = KeyModifiers.None,
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, workbench.ActiveSourceEditor?.TextArea.Caret.Line);
            Assert.Equal(3, workbench.ActiveSourceEditor?.TextArea.Caret.Column);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F12_metadata_definition_opens_labeled_read_only_decompiled_source()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            WorkbenchCodeVirtualDocumentId id = new(new string('a', 64));
            CodeIntelligenceService codeIntelligence = new()
            {
                Definition = snapshot => new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(WorkbenchCodeDestinationKind.Metadata, new("System.String.Empty"),
                        null, null, id)], []),
                VirtualDocument = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    request.Id,
                    WorkbenchCodeVirtualDocumentKind.DecompiledSource,
                    new("String · decompiled"),
                    new("public sealed class String { public int Length => 42; }"),
                    new(new(0, 20), new(0, 26)),
                    new(new("Sample"), new("version"), new("net10.0"), new("Debug"),
                        new("System.Runtime, Version=10.0.0.0"), new(new string('b', 64))),
                    IsReadOnly: true,
                    []),
            };
            LayoutService layouts = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), layouts, codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            workbench.ActiveSourceEditor!.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F12,
                KeyModifiers = KeyModifiers.None,
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, workbench.VirtualDocumentCount);
            Assert.True(workbench.ActiveVirtualEditor!.IsReadOnly);
            Assert.Contains("public sealed class String", workbench.ActiveVirtualEditor.Text,
                StringComparison.Ordinal);
            Assert.Contains("Decompiled source", AutomationProperties.GetName(
                workbench.ActiveVirtualEditor), StringComparison.Ordinal);
            Assert.Contains("read-only", workbench.Documents.ActiveDockable!.Title,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Assert.IsAssignableFrom<Control>(
                        workbench.Documents.ActiveDockable.Context)
                    .GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("Compilation " + new string('b', 64),
                    StringComparison.Ordinal) == true);
            workbench.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.NotNull(layouts.Stored);
            Assert.DoesNotContain("virtual:", layouts.Stored, StringComparison.Ordinal);
            Assert.DoesNotContain("public sealed class String", layouts.Stored,
                StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Exact_context_inspection_opens_a_transient_read_only_document()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Inspection = request => new(
                    request.Snapshot.SessionId, request.Snapshot.Path,
                    request.Snapshot.BufferVersion, WorkbenchCodeResultState.Ready,
                    request.Kind, new("Symbol · Run"), new("Kind: Method\nDisplay: void C.Run()"),
                    new(new("Sample"), new("project-version"), new("net10.0"), new("Debug"),
                        new("Sample, Version=1.0.0.0"), new(new string('c', 64))),
                    IsReadOnly: true, IsTruncated: false, []),
            };
            LayoutService layouts = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), layouts, codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            workbench.InspectActiveDocumentAsync(WorkbenchCodeInspectionKind.Symbol)
                .AsTask().GetAwaiter().GetResult();

            TextEditor editor = Assert.IsType<TextEditor>(workbench.Documents.ActiveDockable!.Context);
            Assert.True(editor.IsReadOnly);
            Assert.Contains("Kind: Method", editor.Text, StringComparison.Ordinal);
            Assert.Contains("read-only", workbench.Documents.ActiveDockable.Title,
                StringComparison.OrdinalIgnoreCase);
            workbench.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.NotNull(layouts.Stored);
            Assert.DoesNotContain("inspection:", layouts.Stored, StringComparison.Ordinal);
            Assert.DoesNotContain("Kind: Method", layouts.Stored, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_toolbar_exposes_intellisense_navigation_usages_and_implementations()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Implementations = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(
                        WorkbenchCodeDestinationKind.Source,
                        new("Example implementation"),
                        snapshot.Path,
                        new(new(0, 5), new(0, 12)))],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            string[] actionLabels =
            [
                "IntelliSense",
                "Symbol info",
                "Definition",
                "Usages",
                "Implementations",
            ];
            Control sourceContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Button[] actions = sourceContent.GetVisualDescendants().OfType<Button>()
                .Where(button => actionLabels.Contains(button.Content?.ToString()))
                .ToArray();
            Assert.Equal(actionLabels.Length, actions.Length);
            Assert.All(actions, action => Assert.True(action.IsEnabled));
            Assert.Contains(actions, action =>
                AutomationProperties.GetName(action) == "Show IntelliSense for src/App.cs");

            Button implementations = Assert.Single(actions, action =>
                Equals(action.Content, "Implementations"));
            implementations.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, codeIntelligence.ImplementationCallCount);
            Assert.Equal(1, workbench.ActiveSourceEditor?.TextArea.Caret.Line);
            Assert.Equal(6, workbench.ActiveSourceEditor?.TextArea.Caret.Column);
            window.Close();
        }, CancellationToken.None);
    }

}
