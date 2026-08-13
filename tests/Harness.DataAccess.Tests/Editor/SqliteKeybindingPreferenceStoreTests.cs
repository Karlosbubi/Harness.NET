using Harness.DataAccess.Configuration;
using Harness.DataAccess.Editor;
using Harness.DataAccess.Persistence;

namespace Harness.DataAccess.Tests.Editor;

public sealed class SqliteKeybindingPreferenceStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-keybinding-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Defaults_custom_rows_and_reset_round_trip_atomically()
    {
        ApplicationPaths paths = new(
            Path.Combine(root, "config"), Path.Combine(root, "data"),
            Path.Combine(root, "state"), Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        StubPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteKeybindingPreferenceStore store = new(applicationPaths);

        StoredKeybindingPreferences initial = await store.GetAsync();
        StoredKeybindingPreferences saved = await store.SaveAsync(new(false,
        [
            new(new("ShowChat"), 0, new("Alt+C")),
            new(new("FindReferences"), 0, new("Shift+F12")),
            new(new("FindReferences"), 1, new("Alt+F7")),
        ], StoredEditorInputMode.Vim));
        StoredKeybindingPreferences reset = await store.ResetAsync();

        Assert.True(initial.UseDefaults);
        Assert.Empty(initial.Bindings);
        Assert.False(saved.UseDefaults);
        Assert.Equal(StoredEditorInputMode.Vim, saved.InputMode);
        Assert.Equal(3, saved.Bindings.Count);
        Assert.Contains(saved.Bindings, binding =>
            binding.Command.Value == "FindReferences" && binding.Position == 1 &&
            binding.Gesture.Value == "Alt+F7");
        Assert.True(reset.UseDefaults);
        Assert.Empty(reset.Bindings);
        Assert.Equal(StoredEditorInputMode.Vim, reset.InputMode);
    }

    [Fact]
    public async Task Rejects_duplicate_positions_before_mutating_storage()
    {
        ApplicationPaths paths = new(
            Path.Combine(root, "config"), Path.Combine(root, "data"),
            Path.Combine(root, "state"), Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        StubPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteKeybindingPreferenceStore store = new(applicationPaths);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.SaveAsync(new(false,
        [
            new(new("ShowChat"), 0, new("Alt+C")),
            new(new("ShowChat"), 0, new("Ctrl+C")),
        ])));
        Assert.True((await store.GetAsync()).UseDefaults);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class StubPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
