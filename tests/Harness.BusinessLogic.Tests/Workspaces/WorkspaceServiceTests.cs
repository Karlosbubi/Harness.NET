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
        Assert.True(registered.Workspace.IsActive);
        Assert.True(trusted.Workspace!.IsTrusted);
        Assert.Equal("feature/configuration", trusted.Workspace.Branch);
        Assert.True(trusted.Workspace.IsDirty);
    }

    [Fact]
    public async Task Refresh_updates_branch_and_dirty_state_without_losing_trust_or_selection()
    {
        string root = Path.GetFullPath("/workspace/repository");
        string entryPoint = Path.Combine(root, "Repository.slnx");
        FakeWorkspaceInspector inspector = new(new(
            root, "repository", "main", false, [entryPoint], null));
        FakeWorkspaceStore store = new();
        WorkspaceService service = new(inspector, store);
        WorkspaceResult registered = await service.RegisterAsync(root, entryPoint);
        await service.SetTrustAsync(registered.Workspace!.Id, true);
        inspector.Inspection = inspector.Inspection with
        {
            Branch = "feature/switched",
            IsDirty = true,
        };

        WorkspaceResult refreshed = await service.RefreshAsync(registered.Workspace.Id);

        Assert.Null(refreshed.Error);
        Assert.Equal("feature/switched", refreshed.Workspace!.Branch);
        Assert.True(refreshed.Workspace.IsDirty);
        Assert.True(refreshed.Workspace.IsTrusted);
        Assert.True(refreshed.Workspace.IsActive);
        Assert.Equal(entryPoint, refreshed.Workspace.EntryPoint);
    }

    private sealed class FakeWorkspaceInspector(WorkspaceInspection inspection)
        : IWorkspaceInspector
    {
        internal WorkspaceInspection Inspection { get; set; } = inspection;

        public ValueTask<WorkspaceInspection> InspectAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Inspection);
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
            bool trusted = workspace?.IsTrusted ?? false;
            bool active = workspace?.IsActive ?? false;
            DateTimeOffset created = workspace?.CreatedAt ?? now;
            workspace = new(
                "workspace-id",
                inspection.RootPath,
                inspection.Name,
                entryPoint,
                trusted,
                active,
                inspection.Branch,
                inspection.IsDirty,
                created,
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

        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(workspace?.IsActive is true ? workspace : null);

        public ValueTask<RegisteredWorkspace> SetActiveAsync(
            string workspaceId,
            CancellationToken cancellationToken = default)
        {
            workspace = (workspace ?? throw new InvalidOperationException()) with
            {
                IsActive = true,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return ValueTask.FromResult(workspace);
        }

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
