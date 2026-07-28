namespace Harness.BusinessLogic.Appearance;

public sealed record ThemeCatalogEntry(
    ThemeId Id,
    string DisplayName,
    ThemeBaseVariant BaseVariant,
    ThemeOrigin Origin,
    IReadOnlyDictionary<ThemeColorToken, ThemeColorValue> Colors);
