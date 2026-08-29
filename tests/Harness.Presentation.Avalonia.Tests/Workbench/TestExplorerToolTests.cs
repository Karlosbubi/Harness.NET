using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Coverage;
using Harness.BusinessLogic.Debugging;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests.Workbench;

[Collection("Avalonia UI")]
public sealed class TestExplorerToolTests
{
    [Fact]
    public async Task Exact_test_debug_uses_the_typed_owned_test_lifecycle_and_opens_debugger()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            WorkbenchCodeTestCase test = TestCase(
                new string('a', 64), "Example.Tests.CalculatorTests.Adds", "Adds", 12, false);
            ExecutionService execution = new(canDebugTest: true);
            DebuggerService debugger = new();
            DeveloperDebugSessionView? shown = null;
            TestExplorerTool tool = new(
                new(new NullInspectionService(), () => shell, () => false,
                    async operation => await operation(), (_, _) => ValueTask.CompletedTask,
                    CancellationToken.None),
                new PresentationControlTests.CodeIntelligenceService(),
                execution,
                (_, _) => ValueTask.CompletedTask,
                () => { },
                () => ValueTask.CompletedTask,
                debugger: debugger,
                showDebugger: value => { shown = value; return ValueTask.CompletedTask; });

            tool.StartTestDebugAsync(
                new(new(test.ProjectPath.Value), null, null), test)
                .AsTask().GetAwaiter().GetResult();

            DeveloperTestDebugStartRequest request = Assert.Single(debugger.Requests);
            Assert.Equal(test.Id.Value, request.Test.Id.Value);
            Assert.Equal(test.FullyQualifiedName.Value, request.Test.FullyQualifiedName.Value);
            Assert.Same(debugger.Session, shown);
            Assert.Contains("owned testhost", tool.StatusText, StringComparison.Ordinal);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Coverage_import_projects_provenance_and_navigates_exact_uncovered_line()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            CoverageService coverage = new();
            DeveloperCoverageLine? navigated = null;
            TestExplorerTool tool = new(
                new(
                    new NullInspectionService(),
                    () => shell,
                    () => false,
                    async operation => await operation(),
                    (_, _) => ValueTask.CompletedTask,
                    CancellationToken.None),
                new PresentationControlTests.CodeIntelligenceService(),
                execution: null,
                (_, _) => ValueTask.CompletedTask,
                () => { },
                () => ValueTask.CompletedTask,
                coverage,
                (line, goalId) =>
                {
                    Assert.Null(goalId);
                    navigated = line;
                    return ValueTask.CompletedTask;
                });
            tool.Update(shell);
            Window window = new() { Width = 800, Height = 700, Content = tool.Content };
            window.Show();
            tool.Coverage.ReportPath.Text = "artifacts/coverage.xml";

            tool.Coverage.ImportAsync().AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            DeveloperCoverageImportRequest request = Assert.Single(coverage.Imports);
            Assert.Equal("workspace-1", request.Workspace.WorkspaceId.Value);
            Assert.Equal("artifacts/coverage.xml", request.ReportPath.Value);
            CoverageTool.CoverageTreeNode source = Assert.Single(
                Assert.IsAssignableFrom<IEnumerable<CoverageTool.CoverageTreeNode>>(
                    tool.Coverage.Tree.ItemsSource));
            Assert.Contains("1/2 lines", source.Label, StringComparison.Ordinal);
            CoverageTool.CoverageTreeNode uncovered = Assert.Single(source.Children);
            Assert.Equal(18, uncovered.Line?.Line.Value);
            Assert.Contains("Cobertura", tool.Coverage.StatusText, StringComparison.Ordinal);
            Assert.Contains("coverlet 6.0.4", tool.Coverage.StatusText,
                StringComparison.Ordinal);
            Assert.Equal("Coverage report path",
                AutomationProperties.GetName(tool.Coverage.ReportPath));
            Assert.Equal("Import Cobertura coverage",
                AutomationProperties.GetName(tool.Coverage.Import));
            Assert.Equal("Coverage source hierarchy",
                AutomationProperties.GetName(tool.Coverage.Tree));
            Assert.Equal("Open coverage source src/Example.cs",
                AutomationProperties.GetName(tool.Coverage.CreateNodeControl(source)));
            Assert.Equal("Open uncovered line 18 in src/Example.cs",
                AutomationProperties.GetName(tool.Coverage.CreateNodeControl(uncovered)));

            tool.Coverage.NavigateAsync(uncovered).AsTask().GetAwaiter().GetResult();
            Assert.Same(uncovered.Line, navigated);
            Assert.Equal(new WorkbenchCodePosition(17, 0),
                DocumentsHost.CoveragePosition(uncovered.Line!));
            window.Close();
        }, CancellationToken.None);
    }

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
            DeveloperProjectPath projectPath = new(first.ProjectPath.Value);
            DeveloperExecutionView typeHistory = Execution(
                DeveloperTestTarget.ForType(
                    projectPath, new("Example.Tests.CalculatorTests")),
                projectPath.Value, "execution-type", DeveloperExecutionState.Succeeded, 0, 420);
            DeveloperExecutionView projectHistory = Execution(
                DeveloperTestTarget.ForProject(projectPath),
                projectPath.Value, "execution-project", DeveloperExecutionState.Failed, 1, 930);
            execution.History.AddRange([failed, running, typeHistory, projectHistory]);
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
            Assert.Equal(DeveloperTestScope.Project, project.Selection?.Scope);
            Assert.Equal(project.Label, project.Selection?.FullyQualifiedName.Value);
            Assert.Same(projectHistory, project.Execution);
            TestExplorerTool.TestTreeNode type = Assert.Single(project.Children);
            Assert.Equal("Example.Tests.CalculatorTests", type.Label);
            Assert.Equal(DeveloperTestScope.Type, type.Selection?.Scope);
            Assert.Equal(type.Label, type.Selection?.FullyQualifiedName.Value);
            Assert.Same(typeHistory, type.Execution);
            Assert.Equal(["Adds values", "Subtracts values"],
                type.Children.Select(node => node.Label));
            Assert.Same(first, type.Children[0].Test);
            Assert.Same(failed, type.Children[0].Execution);
            Assert.Same(running, type.Children[1].Execution);
            Assert.Contains("1 failed", TestExplorerTool.History(failed),
                StringComparison.Ordinal);
            Assert.Contains("2 test(s) discovered", tool.StatusText, StringComparison.Ordinal);
            Assert.Equal("Roslyn test hierarchy", AutomationProperties.GetName(tool.Tree));
            Assert.Equal("Test Explorer search", AutomationProperties.GetName(tool.Filter));
            Assert.Equal("Test framework filter",
                AutomationProperties.GetName(tool.FrameworkFilter));
            Assert.Equal("Test lifecycle state filter",
                AutomationProperties.GetName(tool.StateFilter));
            Assert.Equal("Run selected tests",
                AutomationProperties.GetName(tool.RunSelected));

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
            tool.StartSelectionAsync(project.Project!, project.Selection!, project.Label)
                .AsTask().GetAwaiter().GetResult();
            Assert.Equal(DeveloperTestScope.Project, execution.Tests[^1].Test.Scope);
            tool.StartSelectionAsync(type.Project!, type.Selection!, type.Label)
                .AsTask().GetAwaiter().GetResult();
            Assert.Equal(DeveloperTestScope.Type, execution.Tests[^1].Test.Scope);
            Assert.Equal(3, shown);
            Assert.Equal(3, refreshed);
            Assert.True(tool.SelectTestForRun(first, true));
            Assert.True(tool.SelectTestForRun(second, true));
            Assert.True(tool.RunSelected.IsEnabled);
            Assert.Equal("Run selected (2)", tool.RunSelected.Content);
            tool.StartSelectedAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal(DeveloperTestScope.Selection, execution.Tests[^1].Test.Scope);
            Assert.Equal([
                first.FullyQualifiedName.Value, second.FullyQualifiedName.Value,
            ], execution.Tests[^1].Test.SelectedTests.Select(item => item.Value));
            Assert.Equal(4, shown);
            Assert.Equal(4, refreshed);
            tool.CancelTestAsync(running).AsTask().GetAwaiter().GetResult();
            Assert.Equal("execution-running", Assert.Single(execution.Cancelled).Value);
            Assert.Equal(5, refreshed);
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
        long duration) => Execution(
            new(new(test.Id.Value), new(test.FullyQualifiedName.Value)),
            test.ProjectPath.Value,
            id,
            state,
            exitCode,
            duration);

    private static DeveloperExecutionView Execution(
        DeveloperTestTarget test,
        string projectPath,
        string id,
        DeveloperExecutionState state,
        int? exitCode,
        long duration) => new(
            new(id),
            new("workspace-1"),
            GoalId: null,
            "Original workspace",
            DeveloperExecutionOperation.Test,
            new(new(projectPath), null, null),
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
            test,
            state is DeveloperExecutionState.Running
                ? []
                : [new(new(test.FullyQualifiedName.Value), new(test.FullyQualifiedName.Value),
                    state is DeveloperExecutionState.Failed
                        ? DeveloperTestOutcome.Failed
                        : DeveloperTestOutcome.Passed,
                    duration)]);

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

    private sealed class ExecutionService(bool canDebugTest = false) : IDeveloperProjectExecutionService
    {
        internal List<DeveloperTestStartRequest> Tests { get; } = [];
        internal List<DeveloperExecutionView> History { get; } = [];
        internal List<DeveloperExecutionId> Cancelled { get; } = [];
        public DeveloperExecutionCapabilities Capabilities { get; } = new(
            true, true, true, canDebugTest, "Debug unavailable.", CanTest: true,
            CanDebugTest: canDebugTest);

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

    private sealed class DebuggerService : IDeveloperDebuggerService
    {
        internal List<DeveloperTestDebugStartRequest> Requests { get; } = [];
        internal DeveloperDebugSessionView Session { get; } = new(
            new("test-debug"), new("workspace-1"), null, "Original workspace",
            new(new("tests/App.Tests.csproj"), null, null), null,
            DeveloperDebugSessionState.Running, DeveloperDebugStopReason.None,
            null, null, DateTimeOffset.UnixEpoch, null, "Debugging test…", [], [], [],
            new(string.Empty), false,
            new(new(new string('a', 64)), new("Example.Tests.CalculatorTests.Adds")));

        public ValueTask<DeveloperDebugStartResult> StartTestAsync(
            DeveloperTestDebugStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new DeveloperDebugStartResult(Session, null, null));
        }

        public ValueTask<DeveloperDebugStartResult> StartAsync(
            DeveloperDebugStartRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<DeveloperDebugSessionResult> GetAsync(
            DeveloperDebugSessionId sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<DeveloperDebugSessionResult> CommandAsync(
            DeveloperDebugSessionId sessionId, DeveloperDebugCommand command,
            DeveloperDebugThreadId threadId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<DeveloperDebugSessionResult> StopAsync(
            DeveloperDebugSessionId sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<DeveloperDebugInspectionResult<DeveloperDebugScope>> GetScopesAsync(
            DeveloperDebugSessionId sessionId, DeveloperDebugStackFrameId frameId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DeveloperDebugInspectionResult<DeveloperDebugVariable>> GetVariablesAsync(
            DeveloperDebugSessionId sessionId,
            DeveloperDebugVariablesReference variablesReference,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CoverageService : IDeveloperCoverageService
    {
        internal List<DeveloperCoverageImportRequest> Imports { get; } = [];

        public ValueTask<DeveloperCoverageResult> ImportAsync(
            DeveloperCoverageImportRequest request,
            CancellationToken cancellationToken = default)
        {
            Imports.Add(request);
            return ValueTask.FromResult(Result());
        }

        public ValueTask<DeveloperCoverageResult> GetLatestAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Result());

        private static DeveloperCoverageResult Result() => new(new(
            new("coverage-1"), new("workspace-1"), null, new("Original workspace"),
            new("artifacts/coverage.xml"), new(new string('c', 64)),
            DeveloperCoverageFormat.Cobertura, new("coverlet"), new("6.0.4"),
            DateTimeOffset.Parse("2026-08-29T11:00:00Z"),
            DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
            UnmappedFileCount: 0, IsTruncated: false,
            [
                new(new("src/Example.cs"), new(12), new(4)),
                new(new("src/Example.cs"), new(18), new(0)),
            ]), null, null);
    }
}
