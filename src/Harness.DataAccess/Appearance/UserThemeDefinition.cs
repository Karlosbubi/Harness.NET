namespace Harness.DataAccess.Appearance;

public sealed record UserThemeDefinition(
    ThemeId Id,
    string DisplayName,
    ThemeBaseVariant BaseVariant,
    IReadOnlyDictionary<ThemeColorToken, ThemeColorValue> Colors);
