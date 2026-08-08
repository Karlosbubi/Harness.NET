using Harness.DataAccess.Configuration;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Persistence;

namespace Harness.DataAccess.Tests.Goals;

public sealed class SqliteRemoteSpendPreferenceStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(), "harness-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Initializes_unlimited_and_roundtrips_cost_control_modes()
    {
        ApplicationPaths paths = new(
            Path.Combine(testDirectory, "config"),
            Path.Combine(testDirectory, "data"),
            Path.Combine(testDirectory, "state"),
            Path.Combine(testDirectory, "cache"),
            Path.Combine(testDirectory, "data", "harness.db"),
            Path.Combine(testDirectory, "state", "logs"),
            Path.Combine(testDirectory, "state", "worktrees"));
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteRemoteSpendPreferenceStore store = new(applicationPaths);

        StoredRemoteSpendPreference initial = await store.GetAsync();
        StoredRemoteSpendPreference capped = await store.SaveAsync(new(
            StoredRemoteSpendMode.Capped,
            7_500_000));
        StoredRemoteSpendPreference localOnly = await store.SaveAsync(new(
            StoredRemoteSpendMode.LocalOnly,
            CapMicrousd: null));

        Assert.Equal(StoredRemoteSpendMode.Unlimited, initial.Mode);
        Assert.Null(initial.CapMicrousd);
        Assert.Equal(7_500_000, capped.CapMicrousd);
        Assert.Equal(StoredRemoteSpendMode.LocalOnly, localOnly.Mode);
        Assert.Null(localOnly.CapMicrousd);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
