using Harness.BusinessLogic.Appearance;
using DataAppearance = Harness.DataAccess.Appearance;

namespace Harness.BusinessLogic.Tests.Appearance;

public sealed class AppearanceServiceTests
{
    private static readonly AppearanceOptions Options = new(
    [
        new(new("system"), "System", ThemeBaseVariant.System),
        new(new("harness.light"), "Harness Light", ThemeBaseVariant.Light),
        new(new("harness.dark"), "Harness Dark", ThemeBaseVariant.Dark),
        new(new("harness.high-contrast"), "Harness High Contrast", ThemeBaseVariant.HighContrast),
    ]);

    [Fact]
    public async Task Adds_valid_user_theme_and_persists_selection()
    {
        PreferenceStore preferences = new("system");
        ThemeSource source = new(new(
        [
            new(new("nord"), "Nord", DataAppearance.ThemeBaseVariant.Dark,
                new Dictionary<DataAppearance.ThemeColorToken, DataAppearance.ThemeColorValue>()),
        ], []));
        AppearanceService service = new(preferences, source, Options);

        AppearanceSelectionResult result = await service.SelectAsync(new("nord"));

        Assert.True(result.WasSelected);
        Assert.Equal("nord", preferences.Selected.Value);
        Assert.Equal("nord", result.Snapshot.EffectiveThemeId.Value);
    }

    [Fact]
    public async Task Excludes_low_contrast_theme()
    {
        ThemeSource source = new(new(
        [
            new(new("invisible"), "Invisible", DataAppearance.ThemeBaseVariant.Dark,
                new Dictionary<DataAppearance.ThemeColorToken, DataAppearance.ThemeColorValue>
                {
                    [DataAppearance.ThemeColorToken.TextPrimary] = new("#15151B"),
                }),
        ], []));
        AppearanceService service = new(new PreferenceStore("invisible"), source, Options);

        AppearanceSnapshot snapshot = await service.GetAsync();

        Assert.DoesNotContain(snapshot.Themes, theme => theme.Id.Value == "invisible");
        Assert.Equal("system", snapshot.EffectiveThemeId.Value);
        Assert.Contains(snapshot.Issues, issue => issue.Message.Contains("Contrast", StringComparison.Ordinal));
    }

    private sealed class PreferenceStore(string selected) : DataAppearance.IAppearancePreferenceStore
    {
        internal DataAppearance.ThemeId Selected { get; private set; } = new(selected);

        public ValueTask<DataAppearance.ThemeId> GetSelectedThemeAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Selected);

        public ValueTask SaveSelectedThemeAsync(
            DataAppearance.ThemeId themeId,
            CancellationToken cancellationToken = default)
        {
            Selected = themeId;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThemeSource(DataAppearance.UserThemeCatalog catalog)
        : DataAppearance.IUserThemeSource
    {
        public ValueTask<DataAppearance.UserThemeCatalog> ReadAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(catalog);
    }
}
