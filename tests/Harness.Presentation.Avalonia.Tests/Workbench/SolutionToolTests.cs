using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests.Workbench;

[Collection("Avalonia UI")]
public sealed class SolutionToolTests
{
    [Fact]
    public async Task Solution_tool_maps_static_project_metadata_in_the_active_source_context()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            InspectionService inspection = new();
            string? opened = null;
            SolutionTool tool = new(new(
                inspection,
                () => shell,
                () => false,
                async operation => await operation(),
                (path, _) => { opened = path; return ValueTask.CompletedTask; },
                CancellationToken.None));
            Window window = new() { Width = 600, Height = 700, Content = tool.Content };
            window.Show();

            tool.RefreshAsync().AsTask().GetAwaiter().GetResult();
            SolutionTool.SolutionTreeNode root = Assert.Single(
                Assert.IsAssignableFrom<IEnumerable<SolutionTool.SolutionTreeNode>>(
                    tool.Tree.ItemsSource));

            Assert.Equal("Harness.slnx", root.Label);
            Assert.Equal("Harness.slnx", root.Path?.Value);
            Assert.Contains(root.Children, node => node.Label.StartsWith("SDK policy", StringComparison.Ordinal));
            Assert.Contains(root.Children, node => node.Label.Contains(
                "workload manifests available",
                StringComparison.Ordinal));
            Assert.Contains(root.Children, node => node.Label == "Loading issues · 1");
            SolutionTool.SolutionTreeNode project = Assert.Single(
                root.Children,
                node => node.Path is not null);
            Assert.Equal("src/App/App.csproj", project.Path?.Value);
            Assert.Contains(project.Children, node => node.Label == "Target frameworks");
            Assert.Contains(project.Children, node => node.Label == "Configurations");
            Assert.Contains(project.Children, node => node.Label == "Launch profiles · 1");
            Assert.Contains(project.Children, node => node.Label == "Dependencies");
            Assert.EndsWith("startup candidate", project.Label, StringComparison.Ordinal);
            Assert.Contains("1 project(s)", tool.StatusText, StringComparison.Ordinal);
            Assert.Equal(".NET solution project tree", AutomationProperties.GetName(tool.Tree));
            Assert.Equal("workspace-1", Assert.Single(inspection.Requests).WorkspaceId.Value);
            Assert.Null(opened);
            window.Close();
        }, CancellationToken.None);
    }

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
            IsDirty: false);
        return AvaloniaShellState.Initial with
        {
            Workspaces = WorkspaceManagementState.Initial with { Registered = [workspace] },
            IsLoading = false,
        };
    }

    private sealed class InspectionService : IWorkbenchInspectionService
    {
        internal List<WorkbenchWorkspaceRequest> Requests { get; } = [];

        public ValueTask<WorkbenchDotNetInspectionResult> InspectDotNetAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkbenchDotNetInspectionResult(
                new(
                    request.WorkspaceId,
                    request.GoalId,
                    new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace"),
                new(
                    "Harness.slnx",
                    "slnx",
                    new("10.0.100", "latestFeature", false),
                    [new(
                        "src/App/App.csproj",
                        "Microsoft.NET.Sdk",
                        ["net10.0"],
                        "latest",
                        "enable",
                        [new("package", "Avalonia", "11.3.8")],
                        new(
                            DotNetProjectKindView.Executable,
                            [new(new("Debug"), DotNetConfigurationSourceView.Convention)],
                            IsStartupCandidate: true,
                            new(
                                [new(
                                    new("App"),
                                    DotNetLaunchProfileKindView.Project,
                                    LaunchesBrowser: true,
                                    HasCommandLineArguments: true,
                                    [new("APP_ENV")])],
                                ErrorCode: null,
                                Error: null)))],
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null,
                    new(
                        DotNetSdkHealthStateView.Ready,
                        new("10.0.400"),
                        WorkloadManifestsAvailable: true,
                        ErrorCode: null,
                        Error: null),
                    [new(
                        new("src/Missing/Missing.csproj"),
                        DotNetProjectIssueKindView.Missing,
                        "The project file declared by the solution does not exist.")])));
        }

        public ValueTask<WorkbenchFileCatalogResult> ListFilesAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkbenchTextSearchResult> SearchTextAsync(
            WorkbenchWorkspaceRequest request,
            string query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkbenchGitInspectionResult> InspectGitAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
