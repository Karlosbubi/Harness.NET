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
    public async Task Message_card_background_tracks_effective_theme_resources()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Application application = Assert.IsType<PresentationTestApplication>(Application.Current);
            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.Panel)] =
                new SolidColorBrush(Colors.White);
            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.AccentSoft)] =
                new SolidColorBrush(Colors.LightCyan);
            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.Border)] =
                new SolidColorBrush(Colors.Gray);
            Border assistant = new();
            assistant.Classes.Add("message-card");
            Border user = new();
            user.Classes.Add("message-card");
            user.Classes.Add("user");
            Window window = new()
            {
                Content = new StackPanel { Children = { assistant, user } },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(assistant.Background).Color);
            Assert.Equal(Colors.LightCyan, Assert.IsType<SolidColorBrush>(user.Background).Color);

            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.Panel)] =
                new SolidColorBrush(Color.Parse("#1B1B22"));
            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.AccentSoft)] =
                new SolidColorBrush(Color.Parse("#173E43"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                Color.Parse("#1B1B22"),
                Assert.IsType<SolidColorBrush>(assistant.Background).Color);
            Assert.Equal(
                Color.Parse("#173E43"),
                Assert.IsType<SolidColorBrush>(user.Background).Color);
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
            Assert.True(editor.Options.HighlightCurrentLine);
            Assert.True(editor.Options.EnableRectangularSelection);
            Assert.True(editor.Options.AllowScrollBelowDocument);
            Assert.NotNull(editor.Template);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Semantic_editor_renderer_applies_exact_spans_occurrences_and_folding()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            TextEditor editor = CodeEditorView.Create("class Sample\n{\n    int value;\n}\n",
                isReadOnly: false, path: "Sample.cs");
            Window window = new() { Content = editor };
            window.Show();
            using CodeSemanticRenderer renderer = new(editor);
            WorkbenchCodeLens? invoked = null;
            renderer.CodeLensInvoked += (_, args) => invoked = args.Lens;
            renderer.SetPresentation(new(
                new("session"), new("Sample.cs"), new(1), WorkbenchCodeResultState.Ready,
                [new(new(new(0, 0), new(0, 5)), WorkbenchCodeClassificationKind.Keyword),
                 new(new(new(0, 6), new(0, 12)), WorkbenchCodeClassificationKind.Type)],
                [new(new(new(0, 0), new(3, 1)), WorkbenchCodeFoldingKind.Type,
                    new("Sample …"), false)],
                [new(WorkbenchCodeSymbolKind.Class, new("Sample"),
                    new(new(0, 0), new(3, 1)), new(new(0, 6), new(0, 12)), 0)],
                [new(WorkbenchCodeSymbolKind.Class, new("Sample"),
                    new(new(0, 6), new(0, 12)))],
                [new(new(2, 13), WorkbenchCodeInlayHintKind.InferredType,
                    new(": int"), new("Inferred type: int"))],
                [new(new(0, 0), new(0, 6), WorkbenchCodeLensKind.References,
                    new("Find references"), false)],
                false, []));
            renderer.SetOccurrences(
                [new(new(new(2, 8), new(2, 13)), WorkbenchCodeOccurrenceKind.Definition)]);

            Assert.Equal(2, renderer.ClassificationCount);
            Assert.Equal(1, renderer.FoldingCount);
            Assert.Equal(1, renderer.OccurrenceCount);
            Assert.Equal(1, renderer.InlayHintCount);
            Assert.Equal(1, renderer.CodeLensCount);
            VisualLineElementGenerator generator = editor.TextArea.TextView.ElementGenerators[^1];
            Assert.Equal(0, generator.GetFirstInterestedOffset(0));
            InlineObjectElement inline = Assert.IsType<InlineObjectElement>(
                generator.ConstructElement(0));
            Button lens = Assert.Single(
                Assert.IsType<StackPanel>(inline.Element).Children.OfType<Button>());
            lens.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(WorkbenchCodeLensKind.References, invoked?.Kind);
            Assert.Equal(new WorkbenchCodePosition(0, 6), invoked?.Target);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_exposes_presented_code_lenses_as_accessible_actions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDocumentView view = new(
                new("workspace"),
                null,
                null,
                new("src/Program.cs"),
                new("internal static class Program { public static void Main() { } }"),
                null,
                new(62),
                IsTruncated: false,
                WorkbenchDocumentAccess.Editable,
                "Editing the active trusted workspace.",
                ErrorCode: null,
                Error: null);
            using SourceEditorSurface surface = SourceEditorSurface.Create(
                view,
                KeybindingSettingsSnapshot.Default);
            WorkbenchCodeLens expected = new(
                new(0, 0),
                new(0, 47),
                WorkbenchCodeLensKind.Run,
                new("Run project"),
                true);
            WorkbenchCodeLens? invoked = null;
            surface.CodeLensInvoked += (_, args) => invoked = args.Lens;
            Window window = new() { Width = 1280, Height = 800, Content = surface.Control };
            window.Show();

            surface.UpdateDocumentPresentation(new(
                new("session"),
                new("src/Program.cs"),
                new(1),
                WorkbenchCodeResultState.Ready,
                [],
                [],
                [],
                [],
                [],
                [expected],
                false,
                []));

            Button menu = Assert.Single(surface.Control.GetVisualDescendants().OfType<Button>(),
                button => AutomationProperties.GetName(button) == "Show CodeLens actions");
            Assert.True(menu.IsEnabled);
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Border flyoutContent = Assert.IsType<Border>(
                Assert.IsType<Flyout>(menu.Flyout).Content);
            Button action = Assert.Single(
                Assert.IsType<StackPanel>(flyoutContent.Child).Children.OfType<Button>(),
                button => AutomationProperties.GetName(button) == "Run project at line 1");
            Assert.Equal("Run project · L1", action.Content);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(expected, invoked);

            surface.UpdateDocumentPresentation(new(
                new("session"),
                new("src/Program.cs"),
                new(1),
                WorkbenchCodeResultState.Ready,
                [], [], [], [], [], [], false, []));
            Assert.False(menu.IsEnabled);
            Assert.DoesNotContain(
                Assert.IsType<StackPanel>(flyoutContent.Child).Children.OfType<Button>(),
                button => AutomationProperties.GetName(button)?.StartsWith(
                    "Run project at line", StringComparison.Ordinal) is true);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reactivating_a_source_document_recovers_an_initial_presentation_without_actions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            bool ready = false;
            CodeIntelligenceService codeIntelligence = new()
            {
                Presentation = request => ready
                    ? new(
                        request.Snapshot.SessionId,
                        request.Snapshot.Path,
                        request.Snapshot.BufferVersion,
                        WorkbenchCodeResultState.Ready,
                        [], [], [], [], [],
                        [new(new(0, 0), new(0, 6), WorkbenchCodeLensKind.References,
                            new("Find references"), false)],
                        false,
                        [])
                    : new(
                        request.Snapshot.SessionId,
                        request.Snapshot.Path,
                        request.Snapshot.BufferVersion,
                        WorkbenchCodeResultState.Ready,
                        [], [], [], [], [], [], false, []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();

            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            IDockable source = workbench.Documents.ActiveDockable!;
            workbench.OpenFileAsync("src/App.csproj").AsTask().GetAwaiter().GetResult();
            ready = true;
            workbench.ReactivateDocumentForTest(source);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                if (source.Context is Control current &&
                    current.GetVisualDescendants().OfType<Button>().Any(button =>
                        AutomationProperties.GetName(button) == "Show CodeLens actions" &&
                        button.IsEnabled))
                {
                    break;
                }
                Thread.Sleep(10);
            }

            Control content = Assert.IsAssignableFrom<Control>(source.Context);
            Assert.Contains(content.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Show CodeLens actions" &&
                button.IsEnabled);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Comment", UiThemeColorToken.CodeComment)]
    [InlineData("StringInterpolation", UiThemeColorToken.CodeString)]
    [InlineData("Digits", UiThemeColorToken.CodeNumber)]
    [InlineData("MethodCall", UiThemeColorToken.CodeMethod)]
    [InlineData("Preprocessor", UiThemeColorToken.CodePreprocessor)]
    [InlineData("Punctuation", UiThemeColorToken.CodePunctuation)]
    [InlineData("ValueTypeKeywords", UiThemeColorToken.CodeType)]
    [InlineData("Visibility", UiThemeColorToken.CodeKeyword)]
    public void Csharp_highlighting_uses_distinct_semantic_theme_colors(
        string highlightingCategory,
        UiThemeColorToken expected)
    {
        Assert.Equal(expected, CodeEditorView.ThemeTokenFor(highlightingCategory));
    }

}
