using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;

namespace Harness.Presentation.Avalonia;

internal sealed partial class SettingsWindow
{
    private Control AppearancePage()
    {
        ComboBox theme = new() { MinWidth = 260 };
        AutomationProperties.SetName(theme, "Preferred color theme");
        ThemeChoice[] choices = appearance?.Themes
            .Select(item => new ThemeChoice(item.Id.Value, item.DisplayName))
            .ToArray() ?? [];
        suppressThemeSelection = true;
        theme.ItemsSource = choices;
        theme.SelectedItem = choices.FirstOrDefault(item =>
            item.Id == appearance?.PreferredThemeId.Value);
        suppressThemeSelection = false;
        theme.IsEnabled = appearance is not null;
        theme.SelectionChanged += async (_, _) =>
        {
            if (!suppressThemeSelection && theme.SelectedItem is ThemeChoice choice)
            {
                await store.SelectThemeAsync(choice.Id, cancellationToken);
            }
        };

        Button reload = new() { Content = "Reload installed themes" };
        reload.Classes.Add("command");
        AutomationProperties.SetName(reload, "Reload installed color themes");
        reload.Click += async (_, _) => await store.RefreshThemesAsync(cancellationToken);

        TextBlock issues = new()
        {
            Text = appearance is null
                ? "Loading appearance preferences…"
                : appearance.Issues.Count == 0
                    ? "All installed themes are valid."
                    : string.Join("\n", appearance.Issues.Select(issue =>
                        $"{issue.SourceName}: {issue.Message}")),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
        };

        return Page(
            "Appearance & accessibility",
            "Choose the persisted application theme. Changes apply immediately to every workbench surface.",
            new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Color theme", FontWeight = FontWeight.SemiBold },
                    theme,
                    issues,
                    reload,
                },
            });
    }

}
