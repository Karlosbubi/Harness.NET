using System.Reactive.Subjects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Harness.UI.Avalonia;

public sealed class HarnessThemeController : IDisposable
{
    private readonly Dictionary<string, UiThemeDefinition> themes =
        HarnessThemeCatalog.BuiltIns.ToDictionary(theme => theme.Id.Value, StringComparer.Ordinal);
    private readonly BehaviorSubject<UiThemeSnapshot> snapshots = new(
        new(HarnessThemeCatalog.SystemThemeId, HarnessThemeCatalog.DarkThemeId, false));
    private TopLevel? topLevel;
    private IPlatformSettings? platformSettings;
    private UiThemeId preferred = HarnessThemeCatalog.SystemThemeId;

    public IObservable<UiThemeSnapshot> Snapshots => snapshots;

    public IReadOnlyList<UiThemeDefinition> Themes => themes.Values
        .OrderBy(theme => theme.DisplayName, StringComparer.Ordinal)
        .ToArray();

    public void Register(IEnumerable<UiThemeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        HashSet<string> builtIns = HarnessThemeCatalog.BuiltIns
            .Select(theme => theme.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string identifier in themes.Keys.Where(key => !builtIns.Contains(key)).ToArray())
        {
            themes.Remove(identifier);
        }

        foreach (UiThemeDefinition definition in definitions)
        {
            themes[definition.Id.Value] = definition;
        }

        Apply();
    }

    public void Attach(TopLevel value)
    {
        ArgumentNullException.ThrowIfNull(value);
        DetachPlatformSettings();
        topLevel = value;
        platformSettings = value.GetPlatformSettings();
        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged += OnColorValuesChanged;
        }

        Apply();
    }

    public void Select(UiThemeId themeId)
    {
        ArgumentNullException.ThrowIfNull(themeId);
        preferred = themes.ContainsKey(themeId.Value)
            ? themeId
            : HarnessThemeCatalog.SystemThemeId;
        Apply();
    }

    public void Dispose()
    {
        DetachPlatformSettings();
        snapshots.Dispose();
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues values) => Apply();

    private void Apply()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        PlatformColorValues? system = platformSettings?.GetColorValues();
        bool highContrast = system?.ContrastPreference is ColorContrastPreference.High;
        UiThemeDefinition selected;
        if (highContrast)
        {
            selected = themes[HarnessThemeCatalog.HighContrastThemeId.Value];
        }
        else if (preferred == HarnessThemeCatalog.SystemThemeId)
        {
            bool dark = system?.ThemeVariant is PlatformThemeVariant.Dark;
            selected = themes[(dark
                ? HarnessThemeCatalog.DarkThemeId
                : HarnessThemeCatalog.LightThemeId).Value];
        }
        else
        {
            selected = themes.GetValueOrDefault(preferred.Value)
                ?? themes[HarnessThemeCatalog.DarkThemeId.Value];
        }

        UiThemeDefinition baseTheme = selected.BaseVariant is UiThemeBaseVariant.System
            ? HarnessThemeCatalog.BaseFor(UiThemeBaseVariant.Dark)
            : HarnessThemeCatalog.BaseFor(selected.BaseVariant);
        Dictionary<UiThemeColorToken, string> colors = new(baseTheme.Colors);
        foreach ((UiThemeColorToken token, string value) in selected.Colors)
        {
            colors[token] = value;
        }

        foreach ((UiThemeColorToken token, string value) in colors)
        {
            application.Resources[HarnessThemeResources.Key(token)] =
                new SolidColorBrush(Color.Parse(value));
        }

        application.RequestedThemeVariant = selected.BaseVariant is UiThemeBaseVariant.Light
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        snapshots.OnNext(new(preferred, selected.Id, highContrast));
    }

    private void DetachPlatformSettings()
    {
        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged -= OnColorValuesChanged;
        }

        platformSettings = null;
        topLevel = null;
    }
}
