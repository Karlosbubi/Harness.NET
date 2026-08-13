namespace Harness.DataAccess.Editor;

public sealed record StoredKeybindingCommandName(string Value);

public sealed record StoredKeyGestureText(string Value);

public sealed record StoredKeybinding(
    StoredKeybindingCommandName Command,
    int Position,
    StoredKeyGestureText Gesture);

public sealed record StoredKeybindingPreferences(
    bool UseDefaults,
    IReadOnlyList<StoredKeybinding> Bindings);

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
