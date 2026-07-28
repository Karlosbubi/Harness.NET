namespace Harness.UI.Avalonia;

public sealed record UiThemeSnapshot(
    UiThemeId PreferredThemeId,
    UiThemeId EffectiveThemeId,
    bool IsSystemHighContrast);
