using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;
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
            ["model", "provider", "lead", "implementer", "reviewer", "output"], IsAvailable: true),
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
    private ApplicationSettingsState settingsState = ApplicationSettingsState.Initial;
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
            Dispatcher.UIThread.Post(() => Render(state.Appearance, state.Settings)));
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

    private void Render(
        AppearanceSnapshot? snapshot,
        ApplicationSettingsState applicationSettings)
    {
        appearance = snapshot;
        settingsState = applicationSettings;
        if ((categories.SelectedItem as SettingsCategory)?.Id is
            SettingsCategoryId.Appearance or SettingsCategoryId.ModelsAndRoles)
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

        page.Content = category.Id switch
        {
            SettingsCategoryId.Appearance => AppearancePage(),
            SettingsCategoryId.ModelsAndRoles => ModelsAndRolesPage(),
            _ => PlannedPage(category),
        };
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

    private Control ModelsAndRolesPage()
    {
        AgentDefaultsSnapshot? snapshot = settingsState.AgentDefaults;
        Button discover = new()
        {
            Content = snapshot?.Models.Count > 0 ? "Refresh available models" : "Discover available models",
            IsEnabled = !settingsState.IsBusy,
        };
        discover.Classes.Add("command");
        AutomationProperties.SetName(discover, "Discover available agent models");
        discover.Click += async (_, _) => await store.DiscoverAgentDefaultsAsync(cancellationToken);

        StackPanel roles = new() { Spacing = 12 };
        if (snapshot is null)
        {
            roles.Children.Add(new TextBlock
            {
                Text = "Loading agent defaults…",
                Classes = { "muted" },
            });
        }
        else
        {
            foreach (AgentRoleDefault roleDefault in snapshot.Roles.OrderBy(item => item.Role))
            {
                roles.Children.Add(RoleDefaultCard(roleDefault, snapshot.Models));
            }
        }

        string issues = snapshot?.Issues.Count > 0
            ? string.Join("\n", snapshot.Issues.Select(issue =>
                $"{issue.Provider.Value}: {issue.Message}"))
            : string.Empty;
        return Page(
            "Models & roles",
            "Choose ordinary routing and output defaults. A goal can disclose an override when needed.",
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new Border
                    {
                        Classes = { "card", "attention" },
                        Child = new TextBlock
                        {
                            Text = "A remote default is routing preference only. It never authorizes remote spending; each goal still needs its own positive cap and explicit model selection.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    discover,
                    roles,
                    new TextBlock
                    {
                        Text = settingsState.Status ?? issues,
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    },
                },
            });
    }

    private Control RoleDefaultCard(
        AgentRoleDefault roleDefault,
        IReadOnlyList<GoalModelCandidate> candidates)
    {
        ModelChoice[] choices = candidates.Select(candidate => new ModelChoice(candidate)).ToArray();
        ModelChoice? selected = choices.FirstOrDefault(item =>
            item.Candidate.Provider == roleDefault.Provider &&
            item.Candidate.Model == roleDefault.Model);
        ComboBox model = new()
        {
            ItemsSource = choices,
            SelectedItem = selected,
            MinWidth = 260,
            IsEnabled = !settingsState.IsBusy && choices.Length > 0,
            IsVisible = choices.Length > 0,
        };
        AutomationProperties.SetName(model, $"{roleDefault.Role} default model");
        NumericUpDown maximum = new()
        {
            Minimum = 1,
            Maximum = 8192,
            Increment = 256,
            Value = roleDefault.MaximumOutputTokens.Value,
            MinWidth = 150,
            IsEnabled = !settingsState.IsBusy && choices.Length > 0,
            IsVisible = choices.Length > 0,
        };
        AutomationProperties.SetName(maximum, $"{roleDefault.Role} maximum output tokens");
        Button save = new()
        {
            Content = "Save default",
            IsEnabled = !settingsState.IsBusy && selected is not null,
            IsVisible = choices.Length > 0,
        };
        save.Classes.Add("command");
        AutomationProperties.SetName(save, $"Save {roleDefault.Role} agent defaults");
        model.SelectionChanged += (_, _) => save.IsEnabled =
            !settingsState.IsBusy && model.SelectedItem is ModelChoice;
        save.Click += async (_, _) =>
        {
            if (model.SelectedItem is ModelChoice choice && maximum.Value is { } value)
            {
                await store.UpdateAgentDefaultAsync(
                    roleDefault.Role,
                    choice.Candidate,
                    decimal.ToInt32(value),
                    cancellationToken);
            }
        };

        Border unavailable = new()
        {
            Classes = { "editor-access" },
            IsVisible = choices.Length == 0,
            Child = new TextBlock
            {
                Text = "Discover available models to edit this route and token limit.",
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Grid fields = new()
        {
            RowDefinitions = new("Auto,Auto"),
            ColumnDefinitions = new("*,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 10,
            Children = { model, unavailable },
        };
        Grid.SetColumnSpan(model, 2);
        Grid.SetColumnSpan(unavailable, 2);
        Grid.SetRow(maximum, 1);
        fields.Children.Add(maximum);
        Grid.SetRow(save, 1);
        Grid.SetColumn(save, 1);
        fields.Children.Add(save);
        return new Border
        {
            Classes = { "card", "row" },
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = roleDefault.Role.ToString(),
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = $"Effective: {roleDefault.Access} · {roleDefault.Provider.Value}/{roleDefault.Model.Value} · {roleDefault.MaximumOutputTokens.Value} tokens" +
                               (roleDefault.IsPersisted ? " · Saved" : " · Host fallback"),
                        Classes = { "muted" },
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    fields,
                },
            },
        };
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

    private sealed record ModelChoice(GoalModelCandidate Candidate)
    {
        public override string ToString() =>
            $"{Candidate.Provider.Value} / {Candidate.Model.Value} ({Candidate.Access})";
    }
}
