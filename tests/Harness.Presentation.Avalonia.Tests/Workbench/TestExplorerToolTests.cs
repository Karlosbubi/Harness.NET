using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests.Workbench;

[Collection("Avalonia UI")]
public sealed class TestExplorerToolTests
{
    [Fact]
    public async Task Test_explorer_builds_a_searchable_Roslyn_hierarchy_and_navigates_exact_source()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            WorkbenchCodeTestCase first = TestCase(
                "test-1", "Example.Tests.CalculatorTests.Adds", "Adds values", 12, true);
            WorkbenchCodeTestCase second = TestCase(
                "test-2", "Example.Tests.CalculatorTests.Subtracts", "Subtracts values", 20, false);
            PresentationControlTests.CodeIntelligenceService intelligence = new()
            {
                EmitReadyProgress = true,
                TestDiscovery = request => new(
                    request.SessionId,
                    WorkbenchCodeResultState.Ready,
                    [first, second],
                    Continuation: null,
                    IsTruncated: false,
                    []),
            };
            ExecutionService execution = new();
            DeveloperExecutionView failed = Execution(
                first, "execution-failed", DeveloperExecutionState.Failed, 1, 725);
            DeveloperExecutionView running = Execution(
                second, "execution-running", DeveloperExecutionState.Running, null, 0);
            execution.History.AddRange([failed, running]);
            int shown = 0;
            int refreshed = 0;
            WorkbenchCodeTestCase? navigated = null;
            TestExplorerTool tool = new(
                new(
                    new NullInspectionService(),
                    () => shell,
                    () => false,
                    async operation => await operation(),
                    (_, _) => ValueTask.CompletedTask,
                    CancellationToken.None),
                intelligence,
                execution,
                (test, goalId) =>
                {
                    Assert.Null(goalId);
                    navigated = test;
                    return ValueTask.CompletedTask;
                },
                () => shown++,
                () => { refreshed++; return ValueTask.CompletedTask; });
            Window window = new() { Width = 800, Height = 700, Content = tool.Content };
            window.Show();
            tool.Filter.Text = "Fast";
            tool.FrameworkFilter.SelectedIndex = 1;

            tool.RefreshAsync().AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            WorkbenchCodeSessionRequest start = Assert.Single(intelligence.StartRequests);
            Assert.Equal("workspace-1", start.WorkspaceId.Value);
            Assert.Equal("Harness.slnx", start.EntryPoint.Value);
            WorkbenchCodeTestDiscoveryRequest discovery =
                Assert.Single(intelligence.TestDiscoveryRequests);
            Assert.Equal("session-1", discovery.SessionId.Value);
            Assert.Equal("Fast", discovery.Query);
            Assert.Equal(2_000, discovery.MaximumResults);
            Assert.Equal(0, discovery.Offset);
            Assert.Equal(WorkbenchCodeTestFramework.XUnit, discovery.Framework);

            TestExplorerTool.TestTreeNode project = Assert.Single(
                Assert.IsAssignableFrom<IEnumerable<TestExplorerTool.TestTreeNode>>(
                    tool.Tree.ItemsSource));
            Assert.Equal("tests/App.Tests/App.Tests.csproj", project.Label);
            TestExplorerTool.TestTreeNode type = Assert.Single(project.Children);
            Assert.Equal("Example.Tests.CalculatorTests", type.Label);
            Assert.Equal(["Adds values", "Subtracts values"],
                type.Children.Select(node => node.Label));
            Assert.Same(first, type.Children[0].Test);
            Assert.Same(failed, type.Children[0].Execution);
            Assert.Same(running, type.Children[1].Execution);
            Assert.Contains("2 test(s) discovered", tool.StatusText, StringComparison.Ordinal);
            Assert.Equal("Roslyn test hierarchy", AutomationProperties.GetName(tool.Tree));
            Assert.Equal("Test Explorer search", AutomationProperties.GetName(tool.Filter));
            Assert.Equal("Test framework filter",
                AutomationProperties.GetName(tool.FrameworkFilter));
            Assert.Equal("Test lifecycle state filter",
                AutomationProperties.GetName(tool.StateFilter));

            tool.NavigateAsync(first).AsTask().GetAwaiter().GetResult();
            Assert.Same(first, navigated);
            tool.StartTestAsync(first).AsTask().GetAwaiter().GetResult();
            DeveloperTestStartRequest startedTest = Assert.Single(execution.Tests);
            Assert.Equal("workspace-1", startedTest.Workspace.WorkspaceId.Value);
            Assert.Equal(first.ProjectPath.Value, startedTest.Project.ProjectPath.Value);
            Assert.Equal(first.Id.Value, startedTest.Test.Id.Value);
            Assert.Equal(first.FullyQualifiedName.Value,
                startedTest.Test.FullyQualifiedName.Value);
            Assert.Equal(1, shown);
            Assert.Equal(1, refreshed);
            Assert.Contains("Follow it in Run output", tool.StatusText, StringComparison.Ordinal);
            tool.CancelTestAsync(running).AsTask().GetAwaiter().GetResult();
            Assert.Equal("execution-running", Assert.Single(execution.Cancelled).Value);
            Assert.Equal(2, refreshed);
            Assert.Contains("Stopping", tool.StatusText, StringComparison.Ordinal);

            tool.StateFilter.SelectedIndex = 4;
            tool.RefreshAsync().AsTask().GetAwaiter().GetResult();
            TestExplorerTool.TestTreeNode filteredProject = Assert.Single(
                Assert.IsAssignableFrom<IEnumerable<TestExplorerTool.TestTreeNode>>(
                    tool.Tree.ItemsSource));
            TestExplorerTool.TestTreeNode filteredType = Assert.Single(filteredProject.Children);
            Assert.Equal("Adds values", Assert.Single(filteredType.Children).Label);
            Assert.Contains("1 shown", tool.StatusText, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    private static WorkbenchCodeTestCase TestCase(
        string id,
        string fullyQualifiedName,
        string displayName,
        int line,
        bool parameterized) => new(
            new(id),
            new("tests/App.Tests/App.Tests.csproj"),
            WorkbenchCodeTestFramework.XUnit,
            new(fullyQualifiedName),
            new(displayName),
            new("tests/App.Tests/CalculatorTests.cs"),
            new(new(line, 4), new(line, 20)),
            [new(new("Category"), new("Fast"))],
            parameterized);

    private static DeveloperExecutionView Execution(
        WorkbenchCodeTestCase test,
        string id,
        DeveloperExecutionState state,
        int? exitCode,
        long duration) => new(
            new(id),
            new("workspace-1"),
            GoalId: null,
            "Original workspace",
            DeveloperExecutionOperation.Test,
            new(new(test.ProjectPath.Value), null, null),
            EntryPoint: null,
            state,
            DateTimeOffset.Parse("2026-08-29T11:00:00Z"),
            state is DeveloperExecutionState.Running
                ? null
                : DateTimeOffset.Parse("2026-08-29T11:00:00Z").AddMilliseconds(duration),
            exitCode,
            duration,
            StandardOutput: null,
            StandardError: null,
            IsOutputTruncated: false,
            IsErrorTruncated: false,
            IsOutputAvailable: false,
            ErrorCode: state is DeveloperExecutionState.Failed ? "process_failed" : null,
            Error: state is DeveloperExecutionState.Failed ? "The test failed." : null,
            new(new(test.Id.Value), new(test.FullyQualifiedName.Value)));

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

    private sealed class NullInspectionService : IWorkbenchInspectionService
    {
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

        public ValueTask<WorkbenchDotNetInspectionResult> InspectDotNetAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ExecutionService : IDeveloperProjectExecutionService
    {
        internal List<DeveloperTestStartRequest> Tests { get; } = [];
        internal List<DeveloperExecutionView> History { get; } = [];
        internal List<DeveloperExecutionId> Cancelled { get; } = [];
        public DeveloperExecutionCapabilities Capabilities { get; } = new(
            true, true, true, false, "Debug unavailable.", CanTest: true);

        public ValueTask<DeveloperExecutionStartResult> StartTestAsync(
            DeveloperTestStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Tests.Add(request);
            return ValueTask.FromResult(new DeveloperExecutionStartResult(new(
                new("execution-1"),
                request.Workspace.WorkspaceId,
                request.Workspace.GoalId,
                "Original workspace",
                DeveloperExecutionOperation.Test,
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
                Error: null,
                Test: request.Test), null, null));
        }

        public ValueTask<DeveloperExecutionStartResult> StartRunAsync(
            DeveloperExecutionStartRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<DeveloperExecutionStartResult> StartBuildAsync(
            DeveloperBuildStartRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<DeveloperExecutionListResult> ListAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperExecutionListResult(
                History, false, null, null));

        public ValueTask<DeveloperExecutionCancelResult> CancelAsync(
            DeveloperExecutionId executionId,
            CancellationToken cancellationToken = default)
        {
            Cancelled.Add(executionId);
            return ValueTask.FromResult(new DeveloperExecutionCancelResult(true, null, null));
        }
    }
}
