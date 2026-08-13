using Harness.DataAccess.Configuration;
using Harness.DataAccess.Editor;
using Harness.DataAccess.Persistence;

namespace Harness.DataAccess.Tests.Editor;

public sealed class SqliteEditorIntelligencePreferenceStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Initializes_enabled_defaults_and_roundtrips_each_editor_preference()
    {
        ApplicationPaths paths = new(
            Path.Combine(root, "config"), Path.Combine(root, "data"),
            Path.Combine(root, "state"), Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        StubPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteEditorIntelligencePreferenceStore store = new(applicationPaths);

        StoredEditorIntelligencePreferences initial = await store.GetAsync();
        StoredEditorIntelligencePreferences saved = await store.SaveAsync(new(
            false, true, false, true, false, false, true, false, false));

        Assert.Equal(new(true, true, true, true, true, true, true), initial);
        Assert.Equal(new(false, true, false, true, false, false, true, false, false), saved);
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
