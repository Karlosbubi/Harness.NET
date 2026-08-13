using Avalonia.Input;
using Harness.BusinessLogic.Editor;

namespace Harness.Presentation.Avalonia;

internal static class KeybindingInput
{
    internal static KeybindingCommand? Match(
        KeyEventArgs args,
        KeybindingSettingsSnapshot settings,
        IReadOnlyList<KeybindingCommand> commands)
    {
        if (!TryMap(args, out KeybindingGesture? actual) || actual is null)
        {
            return null;
        }

        foreach (KeybindingCommand command in commands)
        {
            if (settings.GesturesFor(command).Contains(actual))
            {
                return command;
            }
        }
        return null;
    }

    internal static bool Matches(
        KeyEventArgs args,
        KeybindingSettingsSnapshot settings,
        KeybindingCommand command) =>
        TryMap(args, out KeybindingGesture? actual) && actual is not null &&
        settings.GesturesFor(command).Contains(actual);

    private static bool TryMap(KeyEventArgs args, out KeybindingGesture? gesture)
    {
        gesture = null;
        KeybindingModifiers modifiers = KeybindingModifiers.None;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= KeybindingModifiers.Control;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= KeybindingModifiers.Alt;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= KeybindingModifiers.Shift;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= KeybindingModifiers.Meta;

        if (!MapKey(args.Key, out KeybindingKey key))
        {
            return false;
        }
        gesture = new(modifiers, key);
        return true;
    }

    private static bool MapKey(Key key, out KeybindingKey mapped)
    {
        string name = key switch
        {
            Key.OemComma => nameof(KeybindingKey.Comma),
            Key.OemPeriod => nameof(KeybindingKey.Period),
            Key.OemQuestion => nameof(KeybindingKey.Slash),
            Key.OemPipe => nameof(KeybindingKey.Backslash),
            Key.OemSemicolon => nameof(KeybindingKey.Semicolon),
            Key.OemQuotes => nameof(KeybindingKey.Quote),
            Key.OemOpenBrackets => nameof(KeybindingKey.LeftBracket),
            Key.OemCloseBrackets => nameof(KeybindingKey.RightBracket),
            Key.OemMinus => nameof(KeybindingKey.Minus),
            Key.OemPlus => nameof(KeybindingKey.Equal),
            _ => key.ToString(),
        };
        return Enum.TryParse(name, ignoreCase: false, out mapped) && Enum.IsDefined(mapped);
    }
}
