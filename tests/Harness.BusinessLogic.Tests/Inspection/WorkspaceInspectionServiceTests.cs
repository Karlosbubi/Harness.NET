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
            reader);

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
            reader);

        WorkspaceFileView result = await service.ReadFileAsync("workspace-id", "Program.cs");

        Assert.Null(result.Error);
        Assert.Equal("source", result.Content);
        Assert.Equal(1, reader.ReadCount);
        Assert.Equal("/workspace/repository", reader.LastRoot);
    }

    [Fact]
    public async Task A_different_workspace_id_cannot_use_the_active_workspace()
    {
        FakeFileReader reader = new();
        WorkspaceInspectionService service = new(
            new FakeWorkspaceStore(CreateWorkspace(isTrusted: true)),
            reader);

        WorkspaceFileView result = await service.ReadFileAsync("other-id", "Program.cs");

        Assert.Equal("workspace_not_active", result.ErrorCode);
        Assert.Equal(0, reader.ReadCount);
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
                6,
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
