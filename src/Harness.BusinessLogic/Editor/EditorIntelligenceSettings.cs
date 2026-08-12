using Harness.DataAccess.Editor;

namespace Harness.BusinessLogic.Editor;

public sealed record EditorIntelligencePreferences(
    bool ShowParameterNameHints,
    bool ShowInferredTypeHints,
    bool ShowReferenceCodeLens,
    bool ShowImplementationCodeLens,
    bool ShowTestCodeLens,
    bool FormatOnPaste,
    bool FormatOnType)
{
    public static EditorIntelligencePreferences Default { get; } = new(
        true, true, true, true, true, true, true);
}

public sealed record EditorIntelligenceSettingsSnapshot(
    EditorIntelligencePreferences Preferences,
    string Status);

public interface IEditorIntelligenceSettingsService
{
    ValueTask<EditorIntelligenceSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<EditorIntelligenceSettingsSnapshot> SaveAsync(
        EditorIntelligencePreferences preferences,
        CancellationToken cancellationToken = default);
}

internal sealed class EditorIntelligenceSettingsService(
    IEditorIntelligencePreferenceStore store) : IEditorIntelligenceSettingsService
{
    public async ValueTask<EditorIntelligenceSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default) =>
        Snapshot(await store.GetAsync(cancellationToken));

    public async ValueTask<EditorIntelligenceSettingsSnapshot> SaveAsync(
        EditorIntelligencePreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        StoredEditorIntelligencePreferences saved = await store.SaveAsync(new(
            preferences.ShowParameterNameHints,
            preferences.ShowInferredTypeHints,
            preferences.ShowReferenceCodeLens,
            preferences.ShowImplementationCodeLens,
            preferences.ShowTestCodeLens,
            preferences.FormatOnPaste,
            preferences.FormatOnType), cancellationToken);
        return Snapshot(saved);
    }

    private static EditorIntelligenceSettingsSnapshot Snapshot(
        StoredEditorIntelligencePreferences preferences) => new(
        new(
            preferences.ShowParameterNameHints,
            preferences.ShowInferredTypeHints,
            preferences.ShowReferenceCodeLens,
            preferences.ShowImplementationCodeLens,
            preferences.ShowTestCodeLens,
            preferences.FormatOnPaste,
            preferences.FormatOnType),
        "Roslyn editor hints and guarded live-buffer formatting are available for trusted C# source buffers.");
}
