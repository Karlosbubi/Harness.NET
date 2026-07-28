namespace Harness.DataAccess.Appearance;

public sealed record UserThemeCatalog(
    IReadOnlyList<UserThemeDefinition> Themes,
    IReadOnlyList<UserThemeIssue> Issues);
