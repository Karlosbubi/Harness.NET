using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Tests.Workspaces;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task Registration_rejects_an_untracked_entry_point()
    {
        string root = Path.GetFullPath("/workspace/repository");
        FakeWorkspaceStore store = new();
        WorkspaceService service = new(
            new FakeWorkspaceInspector(new(
                root,
                "repository",
                "main",
                false,
                [Path.Combine(root, "Repository.slnx")],
                Error: null)),
            store);

        WorkspaceResult result = await service.RegisterAsync(
            root,
            Path.Combine(root, "Untracked.csproj"));

        Assert.Null(result.Workspace);
        Assert.Contains("not a tracked .NET solution", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Registration_is_untrusted_until_explicitly_trusted()
    {
        string root = Path.GetFullPath("/workspace/repository");
        string entryPoint = Path.Combine(root, "Repository.slnx");
        FakeWorkspaceStore store = new();
        WorkspaceService service = new(
            new FakeWorkspaceInspector(new(
                root,
                "repository",
                "feature/configuration",
                true,
                [entryPoint],
                Error: null)),
            store);

        WorkspaceResult registered = await service.RegisterAsync(root, entryPoint);
        WorkspaceResult trusted = await service.SetTrustAsync(
            registered.Workspace!.Id,
            isTrusted: true);

        Assert.False(registered.Workspace.IsTrusted);
        Assert.True(trusted.Workspace!.IsTrusted);
        Assert.Equal("feature/configuration", trusted.Workspace.Branch);
        Assert.True(trusted.Workspace.IsDirty);
    }

    private sealed class FakeWorkspaceInspector(WorkspaceInspection inspection)
        : IWorkspaceInspector
    {
        public ValueTask<WorkspaceInspection> InspectAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(inspection);
    }

    private sealed class FakeWorkspaceStore : IWorkspaceStore
    {
        private RegisteredWorkspace? workspace;

        internal int SaveCount { get; private set; }

        public ValueTask<RegisteredWorkspace> SaveAsync(
            WorkspaceInspection inspection,
            string entryPoint,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            workspace = new(
                "workspace-id",
                inspection.RootPath,
                inspection.Name,
                entryPoint,
                false,
                inspection.Branch,
                inspection.IsDirty,
                now,
                now);
            return ValueTask.FromResult(workspace);
        }

        public ValueTask<RegisteredWorkspace?> FindByPathAsync(
            string rootPath,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(workspace);

        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RegisteredWorkspace>>(
                workspace is null ? [] : [workspace]);

        public ValueTask<RegisteredWorkspace> SetTrustAsync(
            string workspaceId,
            bool isTrusted,
            CancellationToken cancellationToken = default)
        {
            workspace = (workspace ?? throw new InvalidOperationException()) with
            {
                IsTrusted = isTrusted,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return ValueTask.FromResult(workspace);
        }
    }
}
