using Harness.BusinessLogic.Appearance;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal static class AvaloniaThemeMapper
{
    internal static IReadOnlyList<UiThemeDefinition> UserThemes(AppearanceSnapshot snapshot) =>
        snapshot.Themes
            .Where(theme => theme.Origin is ThemeOrigin.User)
            .Select(theme => new UiThemeDefinition(
                new(theme.Id.Value),
                theme.DisplayName,
                theme.BaseVariant is ThemeBaseVariant.Light
                    ? UiThemeBaseVariant.Light
                    : UiThemeBaseVariant.Dark,
                theme.Colors.ToDictionary(
                    item => Enum.Parse<UiThemeColorToken>(item.Key.ToString()),
                    item => item.Value.Value)))
            .ToArray();
}
