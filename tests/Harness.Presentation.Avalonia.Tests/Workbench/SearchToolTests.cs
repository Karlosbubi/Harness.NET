using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests.Workbench;

[Collection("Avalonia UI")]
public sealed class SearchToolTests
{
    [Fact]
    public async Task Search_tool_uses_the_active_source_context_and_reports_bounded_results()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            InspectionService inspection = new();
            string status = string.Empty;
            SearchTool tool = new(
                new(
                    inspection,
                    () => shell,
                    () => false,
                    async operation => await operation(),
                    (_, _) => ValueTask.CompletedTask,
                    CancellationToken.None),
                value => status = value);

            tool.Query.Text = "namespace";
            tool.SearchAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("namespace", inspection.LastQuery);
            Assert.Equal("workspace-1", Assert.Single(inspection.Requests).WorkspaceId.Value);
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(tool.Results.ItemsSource));
            Assert.True(tool.Results.IsVisible);
            Assert.Equal(1, Grid.GetRow(tool.Results));
            Assert.Equal("Original workspace · 1 match(es) in 1 file(s).", status);
            Assert.Equal("Tracked-text search results", AutomationProperties.GetName(tool.Results));
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
        internal string? LastQuery { get; private set; }

        public ValueTask<WorkbenchFileCatalogResult> ListFilesAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkbenchTextSearchResult> SearchTextAsync(
            WorkbenchWorkspaceRequest request,
            string query,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            LastQuery = query;
            return ValueTask.FromResult(new WorkbenchTextSearchResult(
                new(
                    request.WorkspaceId,
                    request.GoalId,
                    new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace"),
                new(
                    [new("src/App.cs", 1, "namespace Example;")],
                    1,
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null)));
        }

        public ValueTask<WorkbenchGitInspectionResult> InspectGitAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
