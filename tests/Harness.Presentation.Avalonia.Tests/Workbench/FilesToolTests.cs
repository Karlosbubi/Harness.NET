using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests.Workbench;

[Collection("Avalonia UI")]
public sealed class FilesToolTests
{
    [Fact]
    public async Task Files_tool_builds_and_filters_a_repository_tree()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            InspectionService inspection = new();
            FilesTool tool = new(CreateContext(inspection, () => shell));
            Window window = new() { Width = 1280, Height = 800, Content = tool.Content };
            window.Show();

            tool.RefreshAsync().AsTask().GetAwaiter().GetResult();
            FilesTool.FileTreeNode[] roots = Assert
                .IsAssignableFrom<IEnumerable<FilesTool.FileTreeNode>>(tool.Tree.ItemsSource)
                .ToArray();

            Assert.Equal(["src", "README.md"], roots.Select(node => node.Name));
            FilesTool.FileTreeNode source = roots[0];
            Assert.Null(source.Path);
            Assert.Equal(["App.cs", "Feature.cs"], source.Children.Select(node => node.Name));
            Assert.Equal("src/App.cs", source.Children[0].Path?.Value);
            Assert.Equal("workspace-1", Assert.Single(inspection.Requests).WorkspaceId.Value);

            tool.Filter.Text = "feature";
            Dispatcher.UIThread.RunJobs();
            roots = Assert
                .IsAssignableFrom<IEnumerable<FilesTool.FileTreeNode>>(tool.Tree.ItemsSource)
                .ToArray();
            source = Assert.Single(roots);
            Assert.Equal("src", source.Name);
            Assert.Equal("Feature.cs", Assert.Single(source.Children).Name);
            Assert.Equal("Repository file tree", AutomationProperties.GetName(tool.Tree));
            window.Close();
        }, CancellationToken.None);
    }

    private static WorkbenchToolContext CreateContext(
        InspectionService inspection,
        Func<AvaloniaShellState> state) =>
        new(
            inspection,
            state,
            () => false,
            async operation => await operation(),
            (_, _) => ValueTask.CompletedTask,
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

    private sealed class InspectionService : IWorkbenchInspectionService
    {
        internal List<WorkbenchWorkspaceRequest> Requests { get; } = [];

        public ValueTask<WorkbenchFileCatalogResult> ListFilesAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkbenchFileCatalogResult(
                Context(request),
                new(
                    [new("src/App.cs"), new("src/Feature.cs"), new("README.md")],
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null)));
        }

        public ValueTask<WorkbenchTextSearchResult> SearchTextAsync(
            WorkbenchWorkspaceRequest request,
            string query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkbenchGitInspectionResult> InspectGitAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkbenchDotNetInspectionResult> InspectDotNetAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static WorkbenchWorkspaceContext Context(WorkbenchWorkspaceRequest request) =>
            new(
                request.WorkspaceId,
                request.GoalId,
                new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace,
                "Original workspace");
    }
}
