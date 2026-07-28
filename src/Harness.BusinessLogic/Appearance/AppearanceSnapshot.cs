namespace Harness.BusinessLogic.Appearance;

public sealed record AppearanceSnapshot(
    ThemeId PreferredThemeId,
    ThemeId EffectiveThemeId,
    IReadOnlyList<ThemeCatalogEntry> Themes,
    IReadOnlyList<ThemeCatalogIssue> Issues);
