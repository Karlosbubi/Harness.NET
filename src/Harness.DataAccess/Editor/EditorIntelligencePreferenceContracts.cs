namespace Harness.DataAccess.Editor;

public sealed record StoredEditorIntelligencePreferences(
    bool ShowParameterNameHints,
    bool ShowInferredTypeHints,
    bool ShowReferenceCodeLens,
    bool ShowImplementationCodeLens,
    bool ShowTestCodeLens,
    bool FormatOnPaste,
    bool FormatOnType,
    bool ShowRunCodeLens = true,
    bool ShowDebugCodeLens = true);

public interface IEditorIntelligencePreferenceStore
{
    ValueTask<StoredEditorIntelligencePreferences> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredEditorIntelligencePreferences> SaveAsync(
        StoredEditorIntelligencePreferences preferences,
        CancellationToken cancellationToken = default);
}
