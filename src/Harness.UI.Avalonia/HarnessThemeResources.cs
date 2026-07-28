namespace Harness.UI.Avalonia;

public static class HarnessThemeResources
{
    public const string Prefix = "Harness.";

    public static string Key(UiThemeColorToken token) => Prefix + token;
}
