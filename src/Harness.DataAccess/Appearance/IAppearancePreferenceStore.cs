namespace Harness.DataAccess.Appearance;

public interface IAppearancePreferenceStore
{
    ValueTask<ThemeId> GetSelectedThemeAsync(CancellationToken cancellationToken = default);

    ValueTask SaveSelectedThemeAsync(
        ThemeId themeId,
        CancellationToken cancellationToken = default);
}
