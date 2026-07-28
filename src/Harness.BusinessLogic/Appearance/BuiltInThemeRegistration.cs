namespace Harness.BusinessLogic.Appearance;

public sealed record BuiltInThemeRegistration(
    ThemeId Id,
    string DisplayName,
    ThemeBaseVariant BaseVariant);
