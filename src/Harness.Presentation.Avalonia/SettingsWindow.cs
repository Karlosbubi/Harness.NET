using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Appearance;

namespace Harness.Presentation.Avalonia;

internal enum SettingsCategoryId
{
    General,
    Editor,
    Appearance,
    ModelsAndRoles,
    PrivacyAndLimits,
    StorageAndRecovery,
    Advanced,
}

internal sealed record SettingsCategory(
    SettingsCategoryId Id,
    string Name,
    string Summary,
    IReadOnlyList<string> SearchTerms,
    bool IsAvailable)
{
    public override string ToString() => Name;
}

internal static class SettingsCatalog
{
    internal static IReadOnlyList<SettingsCategory> All { get; } =
    [
        new(SettingsCategoryId.General, "General", "Workspace and application behavior",
            ["workspace", "startup", "application"], IsAvailable: false),
        new(SettingsCategoryId.Editor, "Editor", "Editing and code intelligence",
            ["font", "code", "roslyn", "completion", "diagnostics"], IsAvailable: false),
        new(SettingsCategoryId.Appearance, "Appearance & accessibility", "Theme and visual preferences",
            ["color", "theme", "contrast", "accessibility"], IsAvailable: true),
        new(SettingsCategoryId.ModelsAndRoles, "Models & roles", "Default routes for agent roles",
            ["model", "provider", "lead", "implementer", "reviewer", "output"], IsAvailable: false),
        new(SettingsCategoryId.PrivacyAndLimits, "Privacy & limits", "Routing and ordinary default limits",
            ["remote", "local", "privacy", "budget", "tokens", "cost"], IsAvailable: false),
        new(SettingsCategoryId.StorageAndRecovery, "Storage & recovery", "Private state, backups, and restore",
            ["database", "backup", "restore", "xdg"], IsAvailable: false),
        new(SettingsCategoryId.Advanced, "Advanced", "Diagnostics and experimental modules",
            ["logs", "telemetry", "experimental", "modules"], IsAvailable: false),
    ];

    internal static IReadOnlyList<SettingsCategory> Filter(string query)
    {
        string term = query.Trim();
        if (term.Length == 0)
        {
            return All;
        }

        return All.Where(category =>
                category.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                category.Summary.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                category.SearchTerms.Any(searchTerm =>
                    searchTerm.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}

/// <summary>
/// Searchable home for ordinary application preferences. Consequential authority such
/// as remote spending deliberately remains on a goal-bound approval surface.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly TextBox search = new();
    private readonly ListBox categories = new();
    private readonly ContentControl page = new();
    private AppearanceSnapshot? appearance;
    private bool suppressThemeSelection;

    internal SettingsWindow(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        Title = "Settings";
        Width = 980;
        Height = 700;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("settings");
        Content = BuildContent();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Appearance)));
        Opened += (_, _) => search.Focus();
        Closed += (_, _) => subscription.Dispose();
        KeyDown += (_, args) =>
        {
            if (args.Key is Key.Escape)
            {
                args.Handled = true;
                Close();
            }
        };
    }

    private Control BuildContent()
    {
        search.PlaceholderText = "Search settings";
        AutomationProperties.SetName(search, "Search settings");
        search.GetObservable(TextBox.TextProperty).Subscribe(_ => ApplyFilter());

        categories.Classes.Add("settings-categories");
        AutomationProperties.SetName(categories, "Settings categories");
        categories.ItemTemplate = new FuncDataTemplate<SettingsCategory>((category, _) =>
            CategoryRow(category), supportsRecycling: true);
        categories.SelectionChanged += (_, _) => RenderSelectedPage();

        StackPanel navigation = new()
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "SETTINGS", Classes = { "eyebrow" } },
                search,
                categories,
            },
        };
        Border navigationSurface = new()
        {
            Classes = { "settings-navigation" },
            Child = navigation,
        };
        Border pageSurface = new()
        {
            Classes = { "settings-page" },
            Child = page,
        };
        Grid root = new()
        {
            ColumnDefinitions = new("280,*"),
            Children = { navigationSurface },
        };
        Grid.SetColumn(pageSurface, 1);
        root.Children.Add(pageSurface);
        ApplyFilter();
        return root;
    }

    private static Control CategoryRow(SettingsCategory category)
    {
        TextBlock availability = new()
        {
            Text = category.IsAvailable ? string.Empty : "Planned",
            Classes = { "muted" },
            FontSize = 10,
        };
        Grid title = new() { ColumnDefinitions = new("*,Auto") };
        title.Children.Add(new TextBlock
        {
            Text = category.Name,
            FontWeight = FontWeight.SemiBold,
        });
        Grid.SetColumn(availability, 1);
        title.Children.Add(availability);
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                title,
                new TextBlock
                {
                    Text = category.Summary,
                    Classes = { "muted" },
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
    }

    private void ApplyFilter()
    {
        SettingsCategoryId? selected = (categories.SelectedItem as SettingsCategory)?.Id;
        IReadOnlyList<SettingsCategory> filtered = SettingsCatalog.Filter(search.Text ?? string.Empty);
        categories.ItemsSource = filtered;
        categories.SelectedItem = filtered.FirstOrDefault(item => item.Id == selected)
                                  ?? filtered.FirstOrDefault(item => item.IsAvailable)
                                  ?? filtered.FirstOrDefault();
        if (filtered.Count == 0)
        {
            page.Content = EmptySearchPage();
        }
    }

    private void Render(AppearanceSnapshot? snapshot)
    {
        appearance = snapshot;
        if ((categories.SelectedItem as SettingsCategory)?.Id is SettingsCategoryId.Appearance)
        {
            RenderSelectedPage();
        }
    }

    private void RenderSelectedPage()
    {
        if (categories.SelectedItem is not SettingsCategory category)
        {
            if (SettingsCatalog.Filter(search.Text ?? string.Empty).Count == 0)
            {
                page.Content = EmptySearchPage();
            }
            return;
        }

        page.Content = category.Id is SettingsCategoryId.Appearance
            ? AppearancePage()
            : PlannedPage(category);
    }

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

    private static Control PlannedPage(SettingsCategory category) => Page(
        category.Name,
        category.Summary,
        new Border
        {
            Classes = { "card" },
            Child = new TextBlock
            {
                Text = category.Id is SettingsCategoryId.PrivacyAndLimits
                    ? "This category is not exposed yet. Remote spending will remain a separate, explicit, goal-bound approval; a saved preference or credential will never authorize it."
                    : "No preferences from this category are exposed in this build yet.",
                TextWrapping = TextWrapping.Wrap,
            },
        });

    private static Control EmptySearchPage() => Page(
        "No settings found",
        "Try a category, setting name, or related term.",
        new TextBlock { Text = "No matching settings.", Classes = { "muted" } });

    private static Control Page(string title, string description, Control content) =>
        new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 24,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = description,
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new Separator(),
                    content,
                },
            },
        };

    private sealed record ThemeChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
