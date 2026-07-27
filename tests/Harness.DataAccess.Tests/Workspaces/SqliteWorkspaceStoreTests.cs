using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workspaces;

namespace Harness.DataAccess.Tests.Workspaces;

public sealed class SqliteWorkspaceStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "harness-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_metadata_and_changes_trust_only_explicitly()
    {
        ApplicationPaths paths = CreatePaths();
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteWorkspaceStore store = new(applicationPaths);
        string root = Path.Combine(testDirectory, "repository");
        string entry = Path.Combine(root, "Sample.slnx");
        WorkspaceInspection inspection = new(
            root,
            "Sample",
            "main",
            IsDirty: false,
            [entry],
            Error: null);

        RegisteredWorkspace registered = await store.SaveAsync(inspection, entry);
        RegisteredWorkspace selected = await store.SetActiveAsync(registered.Id);
        RegisteredWorkspace trusted = await store.SetTrustAsync(registered.Id, isTrusted: true);
        RegisteredWorkspace refreshed = await store.SaveAsync(
            inspection with { Branch = "feature/test", IsDirty = true },
            entry);
        RegisteredWorkspace? found = await store.FindByPathAsync(root);

        Assert.False(registered.IsTrusted);
        Assert.False(registered.IsActive);
        Assert.True(selected.IsActive);
        Assert.True(trusted.IsTrusted);
        Assert.True(trusted.IsActive);
        Assert.True(refreshed.IsTrusted);
        Assert.Equal("feature/test", found?.Branch);
        Assert.True(found?.IsDirty);
        Assert.Single(await store.ListAsync());
        Assert.Equal(registered.Id, (await store.GetActiveAsync())?.Id);
    }

    [Fact]
    public async Task Selecting_a_workspace_replaces_the_previous_active_workspace()
    {
        ApplicationPaths paths = CreatePaths();
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteWorkspaceStore store = new(applicationPaths);
        string firstRoot = Path.Combine(testDirectory, "first");
        string secondRoot = Path.Combine(testDirectory, "second");
        RegisteredWorkspace first = await store.SaveAsync(
            Inspection(firstRoot),
            Path.Combine(firstRoot, "First.slnx"));
        RegisteredWorkspace second = await store.SaveAsync(
            Inspection(secondRoot),
            Path.Combine(secondRoot, "Second.slnx"));

        await store.SetActiveAsync(first.Id);
        RegisteredWorkspace selected = await store.SetActiveAsync(second.Id);
        RegisteredWorkspace? previous = await store.FindByPathAsync(firstRoot);

        Assert.True(selected.IsActive);
        Assert.False(previous?.IsActive);
        Assert.Equal(second.Id, (await store.GetActiveAsync())?.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(testDirectory, "config"),
        Path.Combine(testDirectory, "data"),
        Path.Combine(testDirectory, "state"),
        Path.Combine(testDirectory, "cache"),
        Path.Combine(testDirectory, "data", "harness.db"),
        Path.Combine(testDirectory, "state", "logs"),
        Path.Combine(testDirectory, "state", "worktrees"));

    private static WorkspaceInspection Inspection(string root) => new(
        root,
        Path.GetFileName(root),
        "main",
        IsDirty: false,
        [$"{root}/{Path.GetFileName(root)}.slnx"],
        Error: null);

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
