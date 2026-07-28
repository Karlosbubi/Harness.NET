namespace Harness.UI.Avalonia;

public static class HarnessThemeCatalog
{
    public static UiThemeId SystemThemeId { get; } = new("system");
    public static UiThemeId LightThemeId { get; } = new("harness.light");
    public static UiThemeId DarkThemeId { get; } = new("harness.dark");
    public static UiThemeId HighContrastThemeId { get; } = new("harness.high-contrast");

    public static IReadOnlyList<UiThemeDefinition> BuiltIns { get; } =
    [
        new(SystemThemeId, "System", UiThemeBaseVariant.System,
            new Dictionary<UiThemeColorToken, string>()),
        CreateLight(),
        CreateDark(),
        CreateHighContrast(),
    ];

    internal static UiThemeDefinition BaseFor(UiThemeBaseVariant variant) => variant switch
    {
        UiThemeBaseVariant.Light => BuiltIns[1],
        UiThemeBaseVariant.Dark => BuiltIns[2],
        UiThemeBaseVariant.HighContrast => BuiltIns[3],
        _ => BuiltIns[2],
    };

    private static UiThemeDefinition CreateLight() => new(
        LightThemeId,
        "Harness Light",
        UiThemeBaseVariant.Light,
        Palette(isDark: false));

    private static UiThemeDefinition CreateDark() => new(
        DarkThemeId,
        "Harness Dark",
        UiThemeBaseVariant.Dark,
        Palette(isDark: true));

    private static UiThemeDefinition CreateHighContrast() => new(
        HighContrastThemeId,
        "Harness High Contrast",
        UiThemeBaseVariant.HighContrast,
        new Dictionary<UiThemeColorToken, string>(Palette(isDark: true))
        {
            [UiThemeColorToken.Window] = "#000000",
            [UiThemeColorToken.Header] = "#000000",
            [UiThemeColorToken.Panel] = "#000000",
            [UiThemeColorToken.Editor] = "#000000",
            [UiThemeColorToken.Border] = "#FFFFFF",
            [UiThemeColorToken.BorderStrong] = "#FFFFFF",
            [UiThemeColorToken.TextPrimary] = "#FFFFFF",
            [UiThemeColorToken.TextMuted] = "#FFFFFF",
            [UiThemeColorToken.TextDim] = "#FFFFFF",
            [UiThemeColorToken.Focus] = "#FFFF00",
            [UiThemeColorToken.Accent] = "#00FFFF",
        });

    private static IReadOnlyDictionary<UiThemeColorToken, string> Palette(bool isDark) =>
        new Dictionary<UiThemeColorToken, string>
        {
            [UiThemeColorToken.Window] = isDark ? "#15151B" : "#F7F7FA",
            [UiThemeColorToken.Header] = isDark ? "#202027" : "#EFEFF4",
            [UiThemeColorToken.Panel] = isDark ? "#1B1B22" : "#FFFFFF",
            [UiThemeColorToken.Raised] = isDark ? "#24242D" : "#E8E8EE",
            [UiThemeColorToken.Hover] = isDark ? "#2C2C36" : "#DDDDE6",
            [UiThemeColorToken.Editor] = isDark ? "#17171D" : "#FCFCFE",
            [UiThemeColorToken.Border] = isDark ? "#5F5F6D" : "#777481",
            [UiThemeColorToken.BorderStrong] = isDark ? "#858591" : "#5D5964",
            [UiThemeColorToken.Focus] = isDark ? "#63D7D1" : "#006E73",
            [UiThemeColorToken.TextPrimary] = isDark ? "#F4F1F8" : "#19171D",
            [UiThemeColorToken.TextMuted] = isDark ? "#C1BDC7" : "#57525E",
            [UiThemeColorToken.TextDim] = isDark ? "#AAA6B2" : "#645F6B",
            [UiThemeColorToken.Accent] = isDark ? "#32BFC2" : "#00777B",
            [UiThemeColorToken.AccentStrong] = isDark ? "#63D7D1" : "#005C60",
            [UiThemeColorToken.AccentSoft] = isDark ? "#173E43" : "#C7F1F0",
            [UiThemeColorToken.Success] = isDark ? "#72DFA0" : "#176B3A",
            [UiThemeColorToken.Warning] = isDark ? "#F3C96B" : "#765500",
            [UiThemeColorToken.Danger] = isDark ? "#FF9DAB" : "#A3223D",
            [UiThemeColorToken.Info] = isDark ? "#73CDF5" : "#17648A",
            [UiThemeColorToken.CodeKeyword] = isDark ? "#78BAF8" : "#195A9A",
            [UiThemeColorToken.CodeType] = isDark ? "#70D7D1" : "#006A68",
            [UiThemeColorToken.CodeString] = isDark ? "#F3C77C" : "#7A5000",
            [UiThemeColorToken.DiffAddBackground] = isDark ? "#193C2B" : "#D8F2E2",
            [UiThemeColorToken.DiffAddText] = isDark ? "#B4F5C9" : "#145B31",
            [UiThemeColorToken.DiffRemoveBackground] = isDark ? "#48242E" : "#F7DDE3",
            [UiThemeColorToken.DiffRemoveText] = isDark ? "#FFC2CA" : "#8E1D35",
        };
}
