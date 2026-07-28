using DataAppearance = Harness.DataAccess.Appearance;

namespace Harness.BusinessLogic.Appearance;

internal sealed class AppearanceService(
    DataAppearance.IAppearancePreferenceStore preferenceStore,
    DataAppearance.IUserThemeSource userThemeSource,
    AppearanceOptions options) : IAppearanceService
{
    private static readonly ThemeId SystemThemeId = new("system");

    public async ValueTask<AppearanceSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        DataAppearance.ThemeId selected = await preferenceStore
            .GetSelectedThemeAsync(cancellationToken);
        DataAppearance.UserThemeCatalog source = await userThemeSource.ReadAsync(cancellationToken);
        List<ThemeCatalogIssue> issues = source.Issues
            .Select(issue => new ThemeCatalogIssue(issue.SourceName, issue.Message))
            .ToList();
        List<ThemeCatalogEntry> themes = options.BuiltInThemes
            .Select(theme => new ThemeCatalogEntry(
                theme.Id,
                theme.DisplayName,
                theme.BaseVariant,
                ThemeOrigin.BuiltIn,
                new Dictionary<ThemeColorToken, ThemeColorValue>()))
            .ToList();
        HashSet<string> identifiers = themes
            .Select(theme => theme.Id.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (DataAppearance.UserThemeDefinition userTheme in source.Themes)
        {
            if (!identifiers.Add(userTheme.Id.Value))
            {
                issues.Add(new(userTheme.Id.Value, "Theme id conflicts with a built-in theme."));
                continue;
            }

            ThemeCatalogEntry mapped = Map(userTheme);
            string? contrastError = ThemeContrastValidator.Validate(mapped);
            if (contrastError is not null)
            {
                issues.Add(new(userTheme.Id.Value, contrastError));
                continue;
            }

            themes.Add(mapped);
        }

        ThemeId preferred;
        try
        {
            preferred = new(selected.Value);
        }
        catch (ArgumentException)
        {
            preferred = SystemThemeId;
            issues.Add(new("preferences", "The stored theme id was invalid; System is active."));
        }

        ThemeId effective = themes.Any(theme => theme.Id == preferred)
            ? preferred
            : SystemThemeId;
        if (effective != preferred)
        {
            issues.Add(new(preferred.Value, "Preferred theme is unavailable; System is active."));
        }

        return new(preferred, effective, themes, issues);
    }

    public async ValueTask<AppearanceSelectionResult> SelectAsync(
        ThemeId themeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(themeId);
        AppearanceSnapshot snapshot = await GetAsync(cancellationToken);
        if (!snapshot.Themes.Any(theme => theme.Id == themeId))
        {
            return new(snapshot, false, $"Theme '{themeId.Value}' is unavailable.");
        }

        await preferenceStore.SaveSelectedThemeAsync(
            new(themeId.Value), cancellationToken);
        return new(await GetAsync(cancellationToken), true, null);
    }

    private static ThemeCatalogEntry Map(DataAppearance.UserThemeDefinition theme) => new(
        new(theme.Id.Value),
        theme.DisplayName,
        theme.BaseVariant is DataAppearance.ThemeBaseVariant.Light
            ? ThemeBaseVariant.Light
            : ThemeBaseVariant.Dark,
        ThemeOrigin.User,
        theme.Colors.ToDictionary(
            item => Enum.Parse<ThemeColorToken>(item.Key.ToString()),
            item => new ThemeColorValue(item.Value.Value)));
}
