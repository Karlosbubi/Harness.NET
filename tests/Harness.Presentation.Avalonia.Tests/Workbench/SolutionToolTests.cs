using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests.Workbench;

[Collection("Avalonia UI")]
public sealed class SolutionToolTests
{
    [Fact]
    public async Task Startup_build_loads_solution_metadata_before_selecting_the_project()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            InspectionService inspection = new();
            ExecutionService execution = new();
            SolutionTool tool = new(
                new(
                    inspection,
                    () => shell,
                    () => false,
                    async operation => await operation(),
                    (_, _) => ValueTask.CompletedTask,
                    CancellationToken.None),
                execution);

            tool.StartDefaultBuildAsync(DeveloperExecutionOperation.Build)
                .AsTask().GetAwaiter().GetResult();

            Assert.Single(inspection.Requests);
            Assert.Equal("src/App/App.csproj", Assert.Single(execution.Builds)
                .Project.ProjectPath.Value);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Solution_tool_maps_static_project_metadata_in_the_active_source_context()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            InspectionService inspection = new();
            ExecutionService execution = new();
            string? opened = null;
            int shown = 0;
            int refreshed = 0;
            SolutionTool tool = new(
                new(
                    inspection,
                    () => shell,
                    () => false,
                    async operation => await operation(),
                    (path, _) => { opened = path; return ValueTask.CompletedTask; },
                    CancellationToken.None),
                execution,
                () => shown++,
                () => { refreshed++; return ValueTask.CompletedTask; });
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
            tool.StartDefaultBuildAsync(DeveloperExecutionOperation.Build)
                .AsTask().GetAwaiter().GetResult();
            DeveloperBuildStartRequest build = Assert.Single(execution.Builds);
            Assert.Equal(DeveloperExecutionOperation.Build, build.Operation);
            Assert.Equal("src/App/App.csproj", build.Project.ProjectPath.Value);
            Assert.Equal("Debug", build.Project.Configuration?.Value);
            Assert.Equal(1, shown);
            Assert.Equal(1, refreshed);
            Assert.Null(opened);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class ExecutionService : IDeveloperProjectExecutionService
    {
        internal List<DeveloperBuildStartRequest> Builds { get; } = [];
        public DeveloperExecutionCapabilities Capabilities { get; } = new(
            true, true, true, false, "Debug unavailable.");

        public ValueTask<DeveloperExecutionStartResult> StartRunAsync(
            DeveloperExecutionStartRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<DeveloperExecutionStartResult> StartBuildAsync(
            DeveloperBuildStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Builds.Add(request);
            return ValueTask.FromResult(new DeveloperExecutionStartResult(
                new(
                    new("execution-1"),
                    request.Workspace.WorkspaceId,
                    request.Workspace.GoalId,
                    "Original workspace",
                    request.Operation,
                    request.Project,
                    EntryPoint: null,
                    DeveloperExecutionState.Running,
                    DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                    CompletedAt: null,
                    ExitCode: null,
                    DurationMilliseconds: 0,
                    StandardOutput: null,
                    StandardError: null,
                    IsOutputTruncated: false,
                    IsErrorTruncated: false,
                    IsOutputAvailable: false,
                    ErrorCode: null,
                    Error: null),
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<DeveloperExecutionListResult> ListAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperExecutionListResult([], false, null, null));

        public ValueTask<DeveloperExecutionCancelResult> CancelAsync(
            DeveloperExecutionId executionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperExecutionCancelResult(false, null, null));
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
