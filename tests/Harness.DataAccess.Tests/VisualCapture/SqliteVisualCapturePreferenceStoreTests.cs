using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.VisualCapture;

namespace Harness.DataAccess.Tests.VisualCapture;

public sealed class SqliteVisualCapturePreferenceStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "harness-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Initializes_private_defaults_and_roundtrips_valid_preferences()
    {
        ApplicationPaths paths = new(
            Path.Combine(root, "config"), Path.Combine(root, "data"), Path.Combine(root, "state"),
            Path.Combine(root, "cache"), Path.Combine(root, "data", "harness.db"),
            Path.Combine(root, "state", "logs"), Path.Combine(root, "state", "worktrees"));
        StubPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteVisualCapturePreferenceStore store = new(applicationPaths);

        StoredVisualCapturePreference initial = await store.GetAsync();
        StoredVisualCapturePreference saved = await store.SaveAsync(new(
            false, 12 * 1024 * 1024, 30, 40, true));

        Assert.True(initial.IsEnabled);
        Assert.False(initial.AllowRemoteModelAccess);
        Assert.Equal(5 * 1024 * 1024, initial.MaximumBytes);
        Assert.Equal(12 * 1024 * 1024, saved.MaximumBytes);
        Assert.Equal(30, saved.RetentionDays);
        Assert.Equal(40, saved.MaximumCapturesPerGoal);
        Assert.True(saved.AllowRemoteModelAccess);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
