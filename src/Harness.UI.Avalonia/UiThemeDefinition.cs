namespace Harness.UI.Avalonia;

public sealed record UiThemeDefinition(
    UiThemeId Id,
    string DisplayName,
    UiThemeBaseVariant BaseVariant,
    IReadOnlyDictionary<UiThemeColorToken, string> Colors);
