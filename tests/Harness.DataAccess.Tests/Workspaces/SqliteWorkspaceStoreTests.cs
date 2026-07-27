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
        RegisteredWorkspace trusted = await store.SetTrustAsync(registered.Id, isTrusted: true);
        RegisteredWorkspace refreshed = await store.SaveAsync(
            inspection with { Branch = "feature/test", IsDirty = true },
            entry);
        RegisteredWorkspace? found = await store.FindByPathAsync(root);

        Assert.False(registered.IsTrusted);
        Assert.True(trusted.IsTrusted);
        Assert.True(refreshed.IsTrusted);
        Assert.Equal("feature/test", found?.Branch);
        Assert.True(found?.IsDirty);
        Assert.Single(await store.ListAsync());
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

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
