using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Harness.BusinessLogic.CodeIntelligence;
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
                (test, goalId) =>
                {
                    Assert.Null(goalId);
                    navigated = test;
                    return ValueTask.CompletedTask;
                });
            Window window = new() { Width = 800, Height = 700, Content = tool.Content };
            window.Show();
            tool.Filter.Text = "Fast";

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

            TestExplorerTool.TestTreeNode project = Assert.Single(
                Assert.IsAssignableFrom<IEnumerable<TestExplorerTool.TestTreeNode>>(
                    tool.Tree.ItemsSource));
            Assert.Equal("tests/App.Tests/App.Tests.csproj", project.Label);
            TestExplorerTool.TestTreeNode type = Assert.Single(project.Children);
            Assert.Equal("Example.Tests.CalculatorTests", type.Label);
            Assert.Equal(["Adds values", "Subtracts values"],
                type.Children.Select(node => node.Label));
            Assert.Same(first, type.Children[0].Test);
            Assert.Contains("2 test(s) discovered", tool.StatusText, StringComparison.Ordinal);
            Assert.Equal("Roslyn test hierarchy", AutomationProperties.GetName(tool.Tree));
            Assert.Equal("Test Explorer search", AutomationProperties.GetName(tool.Filter));

            tool.NavigateAsync(first).AsTask().GetAwaiter().GetResult();
            Assert.Same(first, navigated);
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
}
