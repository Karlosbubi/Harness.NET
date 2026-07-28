using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Avalonia.Controls;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed class PresentationControlTests
{
    [Fact]
    public async Task Markdown_content_renders_without_raw_provider_markup()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(PresentationTestApplication));
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
            HeadlessUnitTestSession.StartNew(typeof(PresentationTestApplication));
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
            HeadlessUnitTestSession.StartNew(typeof(PresentationTestApplication));
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
                new InspectionService(),
                () => shell,
                new TextBlock { Text = "Workspace" },
                new TextBlock { Text = "Conversation" },
                new TextBlock { Text = "Goal context" },
                CancellationToken.None);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.Update(shell);
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            Assert.IsType<DockControl>(workbench.Control);
            Assert.Equal(
                ["document.workspace.overview", "document.file.workspace-1.src/App.cs"],
                workbench.Documents.VisibleDockables?.Select(item => item.Id).ToArray() ?? []);
            TextEditor editor = Assert.IsType<TextEditor>(workbench.Documents.ActiveDockable?.Context);
            Assert.Equal("namespace Example;", editor.Text);
            Assert.True(editor.IsReadOnly);
            Assert.NotNull(workbench.Control.Template);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class InspectionService : IWorkspaceInspectionService
    {
        public ValueTask<WorkspaceFileView> ReadFileAsync(
            string workspaceId,
            string relativePath,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceFileView(
                relativePath,
                "namespace Example;",
                18,
                IsTruncated: false,
                ErrorCode: null,
                Error: null));

        public ValueTask<WorkspaceTextSearchView> SearchTextAsync(
            string workspaceId,
            string query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceTextSearchView(
                [new("src/App.cs", 1, "namespace Example;")],
                1,
                IsTruncated: false,
                ErrorCode: null,
                Error: null));

        public ValueTask<WorkspaceGitStateView> InspectGitAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceGitStateView(
                "main",
                "abc123",
                [new("src/App.cs", "modified")],
                "diff --git a/src/App.cs b/src/App.cs",
                IsTruncated: false,
                ErrorCode: null,
                Error: null));

        public ValueTask<WorkspaceDotNetInfoView> InspectDotNetAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceDotNetInfoView(
                "Harness.slnx",
                "solution",
                null,
                [],
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
    }
}
