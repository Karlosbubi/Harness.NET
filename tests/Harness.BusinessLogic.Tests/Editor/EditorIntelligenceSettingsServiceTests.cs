using Harness.BusinessLogic.Editor;
using Harness.DataAccess.Editor;

namespace Harness.BusinessLogic.Tests.Editor;

public sealed class EditorIntelligenceSettingsServiceTests
{
    [Fact]
    public async Task Maps_private_preferences_and_saves_each_independent_switch()
    {
        PreferenceStore store = new(new(true, false, true, false, true, false, true, true, false));
        EditorIntelligenceSettingsService service = new(store);

        EditorIntelligenceSettingsSnapshot initial = await service.GetAsync();
        EditorIntelligenceSettingsSnapshot saved = await service.SaveAsync(new(
            false, true, false, true, false, true, false, false, true));

        Assert.Equal(new(true, false, true, false, true, false, true, true, false), initial.Preferences);
        Assert.Equal(new(false, true, false, true, false, true, false, false, true), saved.Preferences);
        Assert.Equal(new(false, true, false, true, false, true, false, false, true), store.Current);
        Assert.Contains("trusted C#", saved.Status, StringComparison.Ordinal);
    }

    private sealed class PreferenceStore(
        StoredEditorIntelligencePreferences current) : IEditorIntelligencePreferenceStore
    {
        internal StoredEditorIntelligencePreferences Current { get; private set; } = current;

        public ValueTask<StoredEditorIntelligencePreferences> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);

        public ValueTask<StoredEditorIntelligencePreferences> SaveAsync(
            StoredEditorIntelligencePreferences preferences,
            CancellationToken cancellationToken = default)
        {
            Current = preferences;
            return ValueTask.FromResult(Current);
        }
    }
}
