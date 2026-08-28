using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Privacy;
using Harness.Presentation.Avalonia;
using Harness.Presentation.Terminal;
using Harness.UI.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using BusinessThemeBaseVariant = Harness.BusinessLogic.Appearance.ThemeBaseVariant;

namespace Harness.Host;

internal static class PresentationServiceRegistration
{
    internal static IServiceCollection AddHarnessPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IDashboardService, ConversationDashboardService>();
        services.AddSingleton(new AppearanceOptions(HarnessThemeCatalog.BuiltIns
            .Select(theme => new BuiltInThemeRegistration(
                new(theme.Id.Value),
                theme.DisplayName,
                theme.BaseVariant switch
                {
                    UiThemeBaseVariant.System => BusinessThemeBaseVariant.System,
                    UiThemeBaseVariant.Light => BusinessThemeBaseVariant.Light,
                    UiThemeBaseVariant.Dark => BusinessThemeBaseVariant.Dark,
                    UiThemeBaseVariant.HighContrast => BusinessThemeBaseVariant.HighContrast,
                    _ => throw new InvalidOperationException("Unsupported UI theme variant."),
                }))
            .ToArray()));
        services.AddSingleton<IAppearanceService, AppearanceService>();
        services.AddSingleton<IEditorIntelligenceSettingsService,
            EditorIntelligenceSettingsService>();
        services.AddSingleton<IRemoteSpendPreferenceService, RemoteSpendPreferenceService>();
        services.AddSingleton<AvaloniaPresentationStore>();
        services.AddSingleton<HarnessThemeController>();
        services.AddSingleton<IAvaloniaShell, AvaloniaShell>();
        services.AddSingleton<ITerminalShell, TerminalGuiShell>();
        return services;
    }
}
