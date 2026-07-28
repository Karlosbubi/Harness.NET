using System.Globalization;

namespace Harness.BusinessLogic.Appearance;

internal static class ThemeContrastValidator
{
    private static readonly IReadOnlyDictionary<ThemeColorToken, ThemeColorValue> LightBase =
        CreateBase(isDark: false);
    private static readonly IReadOnlyDictionary<ThemeColorToken, ThemeColorValue> DarkBase =
        CreateBase(isDark: true);

    internal static string? Validate(ThemeCatalogEntry theme)
    {
        Dictionary<ThemeColorToken, ThemeColorValue> colors = new(
            theme.BaseVariant is ThemeBaseVariant.Light ? LightBase : DarkBase);
        foreach ((ThemeColorToken token, ThemeColorValue value) in theme.Colors)
        {
            colors[token] = value;
        }

        (ThemeColorToken Foreground, ThemeColorToken Background, double Ratio)[] pairs =
        [
            (ThemeColorToken.TextPrimary, ThemeColorToken.Window, 4.5),
            (ThemeColorToken.TextPrimary, ThemeColorToken.Panel, 4.5),
            (ThemeColorToken.TextPrimary, ThemeColorToken.Editor, 4.5),
            (ThemeColorToken.TextMuted, ThemeColorToken.Window, 4.5),
            (ThemeColorToken.TextMuted, ThemeColorToken.Panel, 4.5),
            (ThemeColorToken.TextDim, ThemeColorToken.Window, 4.5),
            (ThemeColorToken.Focus, ThemeColorToken.Window, 3.0),
            (ThemeColorToken.Accent, ThemeColorToken.Window, 3.0),
            (ThemeColorToken.DiffAddText, ThemeColorToken.DiffAddBackground, 4.5),
            (ThemeColorToken.DiffRemoveText, ThemeColorToken.DiffRemoveBackground, 4.5),
        ];
        foreach ((ThemeColorToken foreground, ThemeColorToken background, double minimum) in pairs)
        {
            double ratio = Contrast(colors[foreground].Value, colors[background].Value);
            if (ratio < minimum)
            {
                return $"Contrast between {foreground} and {background} is " +
                       $"{ratio:F2}:1; at least {minimum:F1}:1 is required.";
            }
        }

        return null;
    }

    private static double Contrast(string first, string second)
    {
        double light = Luminance(first);
        double dark = Luminance(second);
        if (light < dark)
        {
            (light, dark) = (dark, light);
        }

        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(string color)
    {
        double Component(int offset)
        {
            double value = int.Parse(
                color.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Component(1) + 0.7152 * Component(3) + 0.0722 * Component(5);
    }

    private static IReadOnlyDictionary<ThemeColorToken, ThemeColorValue> CreateBase(bool isDark)
    {
        string surface = isDark ? "#15151B" : "#F7F7FA";
        string panel = isDark ? "#1B1B22" : "#FFFFFF";
        string text = isDark ? "#F4F1F8" : "#19171D";
        string muted = isDark ? "#C1BDC7" : "#57525E";
        string dim = isDark ? "#AAA6B2" : "#645F6B";
        return new Dictionary<ThemeColorToken, ThemeColorValue>
        {
            [ThemeColorToken.Window] = new(surface),
            [ThemeColorToken.Header] = new(isDark ? "#202027" : "#EFEFF4"),
            [ThemeColorToken.Panel] = new(panel),
            [ThemeColorToken.Raised] = new(isDark ? "#24242D" : "#E8E8EE"),
            [ThemeColorToken.Hover] = new(isDark ? "#2C2C36" : "#DDDDE6"),
            [ThemeColorToken.Editor] = new(isDark ? "#17171D" : "#FCFCFE"),
            [ThemeColorToken.Border] = new(isDark ? "#5F5F6D" : "#777481"),
            [ThemeColorToken.BorderStrong] = new(isDark ? "#858591" : "#5D5964"),
            [ThemeColorToken.Focus] = new(isDark ? "#63D7D1" : "#006E73"),
            [ThemeColorToken.TextPrimary] = new(text),
            [ThemeColorToken.TextMuted] = new(muted),
            [ThemeColorToken.TextDim] = new(dim),
            [ThemeColorToken.Accent] = new(isDark ? "#32BFC2" : "#00777B"),
            [ThemeColorToken.AccentStrong] = new(isDark ? "#63D7D1" : "#005C60"),
            [ThemeColorToken.AccentSoft] = new(isDark ? "#173E43" : "#C7F1F0"),
            [ThemeColorToken.Success] = new(isDark ? "#72DFA0" : "#176B3A"),
            [ThemeColorToken.Warning] = new(isDark ? "#F3C96B" : "#765500"),
            [ThemeColorToken.Danger] = new(isDark ? "#FF9DAB" : "#A3223D"),
            [ThemeColorToken.Info] = new(isDark ? "#73CDF5" : "#17648A"),
            [ThemeColorToken.CodeKeyword] = new(isDark ? "#78BAF8" : "#195A9A"),
            [ThemeColorToken.CodeType] = new(isDark ? "#70D7D1" : "#006A68"),
            [ThemeColorToken.CodeString] = new(isDark ? "#F3C77C" : "#7A5000"),
            [ThemeColorToken.DiffAddBackground] = new(isDark ? "#193C2B" : "#D8F2E2"),
            [ThemeColorToken.DiffAddText] = new(isDark ? "#B4F5C9" : "#145B31"),
            [ThemeColorToken.DiffRemoveBackground] = new(isDark ? "#48242E" : "#F7DDE3"),
            [ThemeColorToken.DiffRemoveText] = new(isDark ? "#FFC2CA" : "#8E1D35"),
        };
    }
}
