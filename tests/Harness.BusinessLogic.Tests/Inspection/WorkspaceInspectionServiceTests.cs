using Harness.BusinessLogic.Inspection;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Tests.Inspection;

public sealed class WorkspaceInspectionServiceTests
{
    [Fact]
    public async Task Untrusted_workspace_cannot_read_files()
    {
        FakeFileReader reader = new();
        WorkspaceInspectionService service = new(
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: false)),
            reader,
            new FakeTextSearcher(),
            new FakeGitInspector(),
            new FakeDotNetInspector());

        WorkspaceFileView result = await service.ReadFileAsync("workspace-id", "Program.cs");

        Assert.Equal("workspace_not_trusted", result.ErrorCode);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task Trusted_active_workspace_uses_confined_reader()
    {
        FakeFileReader reader = new();
        WorkspaceInspectionService service = new(
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            reader,
            new FakeTextSearcher(),
            new FakeGitInspector(),
            new FakeDotNetInspector());

        WorkspaceFileView result = await service.ReadFileAsync("workspace-id", "Program.cs");

        Assert.Null(result.Error);
        Assert.Equal("source", result.Content);
        Assert.Equal("hash", result.Sha256);
        Assert.Equal(1, reader.ReadCount);
        Assert.Equal("/workspace/repository", reader.LastRoot);
    }

    [Fact]
    public async Task A_different_workspace_id_cannot_use_the_active_workspace()
    {
        FakeFileReader reader = new();
        WorkspaceInspectionService service = new(
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            reader,
            new FakeTextSearcher(),
            new FakeGitInspector(),
            new FakeDotNetInspector());

        WorkspaceFileView result = await service.ReadFileAsync("other-id", "Program.cs");

        Assert.Equal("workspace_not_active", result.ErrorCode);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task Trusted_active_workspace_can_search_tracked_text()
    {
        FakeTextSearcher searcher = new();
        WorkspaceInspectionService service = new(
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileReader(),
            searcher,
            new FakeGitInspector(),
            new FakeDotNetInspector());

        WorkspaceTextSearchView result = await service.SearchTextAsync("workspace-id", "needle");

        Assert.Null(result.Error);
        WorkspaceTextMatchView match = Assert.Single(result.Matches);
        Assert.Equal("Program.cs", match.Path);
        Assert.Equal(1, searcher.SearchCount);
    }

    [Fact]
    public async Task Trusted_active_workspace_can_inspect_git_state()
    {
        FakeGitInspector inspector = new();
        WorkspaceInspectionService service = new(
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileReader(),
            new FakeTextSearcher(),
            inspector,
            new FakeDotNetInspector());

        WorkspaceGitStateView result = await service.InspectGitAsync("workspace-id");

        Assert.Null(result.Error);
        Assert.Equal("main", result.Branch);
        Assert.Single(result.Changes);
        Assert.Equal(1, inspector.InspectCount);
    }

    [Fact]
    public async Task Trusted_active_workspace_can_inspect_dotnet_metadata()
    {
        FakeDotNetInspector inspector = new();
        WorkspaceInspectionService service = new(
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            new FakeFileReader(),
            new FakeTextSearcher(),
            new FakeGitInspector(),
            inspector);

        WorkspaceDotNetInfoView result = await service.InspectDotNetAsync("workspace-id");

        Assert.Null(result.Error);
        DotNetProjectView project = Assert.Single(result.Projects);
        Assert.Equal("net10.0", Assert.Single(project.TargetFrameworks));
        Assert.Equal(1, inspector.InspectCount);
    }

    private static RegisteredWorkspace CreateWorkspace(bool isTrusted) => new(
        "workspace-id",
        "/workspace/repository",
        "repository",
        "/workspace/repository/Repository.slnx",
        isTrusted,
        IsActive: true,
        "main",
        IsDirty: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class FakeFileReader : IWorkspaceFileReader
    {
        internal int ReadCount { get; private set; }
        internal string? LastRoot { get; private set; }

        public ValueTask<WorkspaceFileRead> ReadAsync(
            string workspaceRoot,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            LastRoot = workspaceRoot;
            return ValueTask.FromResult(new WorkspaceFileRead(
                relativePath,
                "source",
                "hash",
                6,
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }
    }

    private sealed class FakeTextSearcher : IWorkspaceTextSearcher
    {
        internal int SearchCount { get; private set; }

        public ValueTask<WorkspaceTextSearch> SearchAsync(
            string workspaceRoot,
            string query,
            CancellationToken cancellationToken = default)
        {
            SearchCount++;
            return ValueTask.FromResult(new WorkspaceTextSearch(
                [new("Program.cs", 12, "// needle")],
                1,
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }
    }

    private sealed class FakeGitInspector : IWorkspaceGitInspector
    {
        internal int InspectCount { get; private set; }

        public ValueTask<WorkspaceGitState> InspectAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            InspectCount++;
            return ValueTask.FromResult(new WorkspaceGitState(
                "main",
                "abc123",
                [new("Program.cs", "ModifiedInWorkdir")],
                "diff --git a/Program.cs b/Program.cs",
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }
    }

    private sealed class FakeDotNetInspector : IWorkspaceDotNetInspector
    {
        internal int InspectCount { get; private set; }

        public ValueTask<WorkspaceDotNetInfo> InspectAsync(
            string workspaceRoot,
            string entryPoint,
            CancellationToken cancellationToken = default)
        {
            InspectCount++;
            return ValueTask.FromResult(new WorkspaceDotNetInfo(
                "Repository.slnx",
                "slnx",
                new("10.0.201", "latestPatch", false),
                [new(
                    "Sample.csproj",
                    "Microsoft.NET.Sdk",
                    ["net10.0"],
                    "latest",
                    "enable",
                    [new("package", "xunit", "2.9.3")])],
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }
    }

    private sealed class FakeWorkspaceStore(RegisteredWorkspace? workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(workspace);

        public ValueTask<RegisteredWorkspace> SaveAsync(
            WorkspaceInspection inspection,
            string entryPoint,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace?> FindByPathAsync(
            string rootPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetActiveAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetTrustAsync(
            string workspaceId,
            bool isTrusted,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
