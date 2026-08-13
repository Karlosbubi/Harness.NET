namespace Harness.DataAccess.Editor;

public sealed record StoredKeybindingCommandName(string Value);

public sealed record StoredKeyGestureText(string Value);

public enum StoredEditorInputMode
{
    Standard,
    Vim,
}

public sealed record StoredKeybinding(
    StoredKeybindingCommandName Command,
    int Position,
    StoredKeyGestureText Gesture);

public sealed record StoredKeybindingPreferences(
    bool UseDefaults,
    IReadOnlyList<StoredKeybinding> Bindings,
    StoredEditorInputMode InputMode = StoredEditorInputMode.Standard);

public interface IKeybindingPreferenceStore
{
    ValueTask<StoredKeybindingPreferences> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredKeybindingPreferences> SaveAsync(
        StoredKeybindingPreferences preferences,
        CancellationToken cancellationToken = default);

    ValueTask<StoredKeybindingPreferences> ResetAsync(
        CancellationToken cancellationToken = default);
}
