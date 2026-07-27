using Harness.DataAccess.Configuration;
using Harness.DataAccess.Framework;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workspaces;

namespace Harness.DataAccess.Tests.Framework;

public sealed class SqliteFrameworkOverlayStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-overlay-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Saves_updates_and_deletes_private_workspace_overlay()
    {
        ApplicationPaths paths = CreatePaths();
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteWorkspaceStore workspaceStore = new(applicationPaths);
        string workspaceRoot = Path.Combine(root, "workspace");
        string entryPoint = Path.Combine(workspaceRoot, "Workspace.slnx");
        RegisteredWorkspace workspace = await workspaceStore.SaveAsync(
            new(
                workspaceRoot,
                "Workspace",
                "main",
                IsDirty: false,
                [entryPoint],
                Error: null),
            entryPoint);
        SqliteFrameworkOverlayStore store = new(applicationPaths);

        WorkspaceFrameworkOverlay created = await store.SaveAsync(
            workspace.Id,
            "Prefer explicit SQL.");
        WorkspaceFrameworkOverlay updated = await store.SaveAsync(
            workspace.Id,
            "Prefer explicit SQL and immutable records.");
        WorkspaceFrameworkOverlay? loaded = await store.GetAsync(workspace.Id);

        Assert.Equal("Prefer explicit SQL.", created.Content);
        Assert.Equal("Prefer explicit SQL and immutable records.", updated.Content);
        Assert.Equal(updated.Content, loaded?.Content);

        await store.DeleteAsync(workspace.Id);
        Assert.Null(await store.GetAsync(workspace.Id));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(root, "config"),
        Path.Combine(root, "data"),
        Path.Combine(root, "state"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
