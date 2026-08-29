using Harness.BusinessLogic.Coverage;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Coverage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.BusinessLogic.Tests.Coverage;

public sealed class DeveloperCoverageServiceTests
{
    [Fact]
    public async Task Imports_and_reads_coverage_in_the_exact_resolved_source_context()
    {
        ContextResolver contexts = new();
        CoverageReader reader = new();
        CoverageStore store = new();
        DeveloperCoverageService service = new(
            contexts, reader, store, new FixedTimeProvider(),
            NullLogger<DeveloperCoverageService>.Instance);
        WorkbenchWorkspaceRequest workspace = new(new("workspace-a"), new("goal-a"));

        DeveloperCoverageResult imported = await service.ImportAsync(new(
            workspace, new("artifacts/coverage.xml")));
        DeveloperCoverageResult latest = await service.GetLatestAsync(workspace);

        DeveloperCoverageView view = Assert.IsType<DeveloperCoverageView>(imported.Coverage);
        Assert.Equal("/worktrees/goal-a", reader.Root);
        Assert.Equal("artifacts/coverage.xml", reader.Path?.Value);
        Assert.Equal("workspace-a", view.WorkspaceId.Value);
        Assert.Equal("goal-a", view.GoalId?.Value);
        Assert.Equal("Approved goal worktree", view.SourceDescription.Value);
        Assert.Equal("src/Example.cs", Assert.Single(view.Lines).Path.Value);
        Assert.Equal(9, view.Lines[0].Hits.Value);
        Assert.Equal(DateTimeOffset.Parse("2026-08-29T13:00:00Z"), view.ImportedAt);
        Assert.Equal(view.Id, latest.Coverage?.Id);
        Assert.Equal(view.Lines.ToArray(), latest.Coverage?.Lines.ToArray());
        Assert.Equal(2, contexts.Requests.Count);
    }

    [Theory]
    [InlineData(" ../coverage.xml")]
    [InlineData("../coverage.xml ")]
    [InlineData("")]
    public async Task Rejects_invalid_report_input_before_resolving_context(string path)
    {
        ContextResolver contexts = new();
        DeveloperCoverageService service = new(
            contexts, new CoverageReader(), new CoverageStore(), new FixedTimeProvider(),
            NullLogger<DeveloperCoverageService>.Instance);

        DeveloperCoverageResult result = await service.ImportAsync(new(
            new(new("workspace-a"), null), new(path)));

        Assert.Equal("coverage_path_invalid", result.ErrorCode);
        Assert.Empty(contexts.Requests);
    }

    private sealed class ContextResolver : IWorkbenchWorkspaceContextResolver
    {
        internal List<WorkbenchWorkspaceRequest> Requests { get; } = [];

        public ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkbenchWorkspaceResolution(
                new(request.WorkspaceId, request.GoalId, new("goal/goal-a"),
                    WorkbenchWorkspaceScope.ApprovedGoalWorktree, "Approved goal worktree"),
                "/worktrees/goal-a", null, null));
        }
    }

    private sealed class CoverageReader : IWorkspaceCoverageReader
    {
        internal string? Root { get; private set; }
        internal CoverageReportPath? Path { get; private set; }

        public ValueTask<WorkspaceCoverageReadResult> ReadAsync(
            string workspaceRoot,
            CoverageReportPath reportPath,
            CancellationToken cancellationToken = default)
        {
            Root = workspaceRoot;
            Path = reportPath;
            return ValueTask.FromResult(new WorkspaceCoverageReadResult(
                reportPath, new(new string('b', 64)), CoverageReportFormat.Cobertura,
                new("coverlet"), new("6.0.4"),
                DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                [new(new("src/Example.cs"), new(21), new(9))],
                UnmappedFileCount: 1, IsTruncated: false, null, null));
        }
    }

    private sealed class CoverageStore : IDeveloperCoverageStore
    {
        private StoredCoverageImport? saved;

        public ValueTask SaveAsync(
            StoredCoverageImport coverage,
            CancellationToken cancellationToken = default)
        {
            saved = coverage;
            return ValueTask.CompletedTask;
        }

        public ValueTask<StoredCoverageImport?> GetLatestAsync(
            StoredCoverageWorkspaceId workspaceId,
            StoredCoverageGoalId? goalId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("workspace-a", workspaceId.Value);
            Assert.Equal("goal-a", goalId?.Value);
            return ValueTask.FromResult(saved);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-08-29T13:00:00Z");
    }
}
