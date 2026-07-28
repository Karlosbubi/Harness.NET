using System.Text.RegularExpressions;

namespace Harness.BusinessLogic.Appearance;

public sealed partial record ThemeId
{
    public ThemeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidThemeId().IsMatch(value))
        {
            throw new ArgumentException("Theme id is invalid.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidThemeId();
}
