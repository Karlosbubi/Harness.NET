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
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;

namespace Harness.Presentation.Avalonia;

internal enum SettingsCategoryId
{
    General,
    Editor,
    Appearance,
    ModelProviders,
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
        new(SettingsCategoryId.ModelProviders, "Model providers", "Ollama and OpenRouter availability",
            ["model", "provider", "ollama", "openrouter", "remote", "local", "pricing"], IsAvailable: true),
        new(SettingsCategoryId.ModelsAndRoles, "Models & roles", "Default routes for agent roles",
            ["model", "provider", "lead", "implementer", "reviewer", "output"], IsAvailable: true),
        new(SettingsCategoryId.PrivacyAndLimits, "Privacy & limits", "Routing and ordinary default limits",
            ["remote", "local", "privacy", "budget", "tokens", "cost", "spend"], IsAvailable: true),
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
            SettingsCategoryId.Appearance or SettingsCategoryId.ModelProviders or
            SettingsCategoryId.ModelsAndRoles or SettingsCategoryId.PrivacyAndLimits)
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
            SettingsCategoryId.ModelProviders => ModelProvidersPage(),
            SettingsCategoryId.ModelsAndRoles => ModelsAndRolesPage(),
            SettingsCategoryId.PrivacyAndLimits => PrivacyAndLimitsPage(),
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
                roles.Children.Add(RoleDefaultCard(
                    roleDefault,
                    snapshot.Models,
                    snapshot.DefaultIssues.FirstOrDefault(issue => issue.Role == roleDefault.Role)));
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
                            Text = "Remote role defaults can run immediately for goals using the Unlimited or Capped spend mode. Use Privacy & limits to opt into a hard cap or local-only default.",
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

    private Control PrivacyAndLimitsPage()
    {
        RemoteSpendPreference current = settingsState.RemoteSpendPreference;
        RemoteSpendChoice[] choices =
        [
            new(RemoteSpendMode.Unlimited, "Unlimited remote spend (default)"),
            new(RemoteSpendMode.Capped, "Set an aggregate spending cap"),
            new(RemoteSpendMode.LocalOnly, "Local models only"),
        ];
        ComboBox mode = new()
        {
            ItemsSource = choices,
            SelectedItem = choices.First(choice => choice.Mode == current.Mode),
            MinWidth = 320,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(mode, "Default remote spending mode");
        TextBox cap = new()
        {
            Text = current.Cap is null
                ? string.Empty
                : GoalPresentationFormatter.ToUsd(current.Cap.Value),
            PlaceholderText = "USD, for example 10.00",
            MinWidth = 240,
            IsEnabled = current.Mode is RemoteSpendMode.Capped && !settingsState.IsBusy,
        };
        AutomationProperties.SetName(cap, "Default remote spending cap in US dollars");
        TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };
        mode.SelectionChanged += (_, _) => cap.IsEnabled =
            mode.SelectedItem is RemoteSpendChoice { Mode: RemoteSpendMode.Capped } &&
            !settingsState.IsBusy;
        Button save = new()
        {
            Content = "Save cost-control default",
            IsEnabled = !settingsState.IsBusy,
        };
        save.Classes.Add("primary");
        AutomationProperties.SetName(save, "Save default remote spending policy");
        save.Click += async (_, _) =>
        {
            if (mode.SelectedItem is not RemoteSpendChoice selected)
            {
                validation.Text = "Choose a remote-spending mode.";
                return;
            }

            MicroUsdAmount? amount = null;
            if (selected.Mode is RemoteSpendMode.Capped)
            {
                if (!TryParseUsd(cap.Text, out amount))
                {
                    validation.Text = "Enter a positive USD cap using a decimal point and at most six decimal places.";
                    return;
                }
            }

            await store.UpdateRemoteSpendPreferenceAsync(
                new(selected.Mode, amount), cancellationToken);
        };

        return Page(
            "Privacy & limits",
            "Choose the spend policy preselected for newly created goals. Every goal creation surface shows the choice again.",
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
                            Text = "Unlimited remote spend is the convenience default. It removes Harness.NET's aggregate dollar ceiling; provider billing and account limits still apply. Opt into a cap or local-only execution here when you want hard cost control.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    new TextBlock { Text = "Default for new goals", FontWeight = FontWeight.SemiBold },
                    mode,
                    new TextBlock { Text = "Aggregate cap (USD)", FontWeight = FontWeight.SemiBold },
                    cap,
                    validation,
                    save,
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
    }

    private Control RoleDefaultCard(
        AgentRoleDefault roleDefault,
        IReadOnlyList<GoalModelCandidate> candidates,
        AgentRoleDefaultIssue? defaultIssue)
    {
        GoalModelCandidate[] choices = ModelSelectionCatalog.ForRole(
            candidates, roleDefault.Role);
        GoalModelCandidate? selected = choices.FirstOrDefault(item =>
            item.Provider == roleDefault.Provider &&
            item.Model == roleDefault.Model);
        SearchableModelPicker model = new()
        {
            MinWidth = 260,
            IsEnabled = !settingsState.IsBusy && choices.Length > 0,
            IsVisible = choices.Length > 0,
        };
        model.SetCandidates(choices, selected);
        model.SetAutomationName($"{roleDefault.Role} default model");
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
            IsEnabled = !settingsState.IsBusy && model.SelectedCandidate is not null,
            IsVisible = choices.Length > 0,
        };
        save.Classes.Add("command");
        AutomationProperties.SetName(save, $"Save {roleDefault.Role} agent defaults");
        model.SelectionChanged += (_, _) => save.IsEnabled =
            !settingsState.IsBusy && model.SelectedCandidate is not null;
        save.Click += async (_, _) =>
        {
            if (model.SelectedCandidate is { } candidate && maximum.Value is { } value)
            {
                await store.UpdateAgentDefaultAsync(
                    roleDefault.Role,
                    candidate,
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
                        Text = defaultIssue is null
                            ? $"Effective: {roleDefault.Access} · {roleDefault.Provider.Value}/{roleDefault.Model.Value} · {roleDefault.MaximumOutputTokens.Value} tokens" +
                              (roleDefault.IsPersisted ? " · Saved" : " · Host fallback")
                            : $"Needs attention: {defaultIssue.Message}",
                        Classes = { "muted" },
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    fields,
                },
            },
        };
    }

    private Control ModelProvidersPage()
    {
        AgentDefaultsSnapshot? snapshot = settingsState.AgentDefaults;
        Button refresh = new()
        {
            Content = "Refresh provider catalogs",
            IsEnabled = !settingsState.IsBusy,
        };
        refresh.Classes.Add("command");
        AutomationProperties.SetName(refresh, "Refresh Ollama and OpenRouter model catalogs");
        refresh.Click += async (_, _) => await store.DiscoverAgentDefaultsAsync(cancellationToken);

        StackPanel providers = new() { Spacing = 12 };
        if (snapshot is null)
        {
            providers.Children.Add(new TextBlock
            {
                Text = "Detecting configured model providers…",
                Classes = { "muted" },
            });
        }
        else
        {
            ModelProviderName[] providerNames = snapshot.Providers
                .Select(item => item.Provider)
                .Concat(settingsState.ProviderSettings?.Providers.Select(item => item.Provider) ?? [])
                .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (ModelProviderName providerName in providerNames)
            {
                ModelProviderSettingsView? configuration = settingsState.ProviderSettings?.Providers
                    .FirstOrDefault(item => item.Provider.Value.Equals(
                        providerName.Value,
                        StringComparison.OrdinalIgnoreCase));
                AgentModelProviderStatus provider = snapshot.Providers.FirstOrDefault(item =>
                    item.Provider.Value.Equals(providerName.Value, StringComparison.OrdinalIgnoreCase)) ?? new(
                    providerName,
                    configuration?.Kind is AgentModelProviderKind.OpenRouter
                        ? ModelAccess.Remote
                        : ModelAccess.Local,
                    configuration?.ChatModel ?? new("unknown"),
                    DiscoveredChatModels: 0,
                    RoleCompatibleModels: 0,
                    HasPublishedPricing: false,
                    AgentModelProviderAvailability.Empty,
                    "No catalog status is available.");
                providers.Children.Add(ProviderCard(provider, configuration));
            }
        }

        return Page(
            "Model providers",
            "Configure named Ollama and OpenRouter modules. Catalog discovery runs without inference; endpoint and model changes are written to your private XDG configuration and apply after restart.",
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
                            Text = "Saved values update the private XDG override. Environment and command-line overrides retain higher precedence. Provider routes and embedding partition identity change only after restart.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    refresh,
                    providers,
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    },
                },
            });
    }

    private Control ProviderCard(
        AgentModelProviderStatus provider,
        ModelProviderSettingsView? configuration)
    {
        string pricing = provider.Access is ModelAccess.Remote
            ? provider.HasPublishedPricing
                ? "Published pricing available for discovered models"
                : "Published pricing unavailable; remote execution remains fail-closed"
            : "Local execution; no remote spending authority";
        StackPanel content = new()
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = provider.Provider.Value,
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = $"{provider.Access} · {provider.Availability} · configured default {provider.ConfiguredDefaultModel.Value}",
                    Classes = { "muted" },
                },
                new TextBlock
                {
                    Text = $"{provider.DiscoveredChatModels} chat model(s) · " +
                           $"{provider.RoleCompatibleModels} fully role-compatible · {pricing}",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = provider.Message ?? "Catalog ready.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                },
            },
        };

        if (configuration is null)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Configuration controls are unavailable in this host.",
                Classes = { "muted" },
            });
            return new Border { Classes = { "card", "row" }, Child = content };
        }

        TextBox endpoint = ProviderTextBox(configuration.Endpoint.Value,
            $"{provider.Provider.Value} endpoint");
        TextBox chatModel = ProviderTextBox(configuration.ChatModel.Value,
            $"{provider.Provider.Value} default chat model");
        TextBox embeddingModel = ProviderTextBox(configuration.EmbeddingModel.Value,
            $"{provider.Provider.Value} default embedding model");
        NumericUpDown dimensions = ProviderNumber(
            configuration.EmbeddingDimensions.Value, 1, 65_536,
            $"{provider.Provider.Value} embedding dimensions");
        NumericUpDown connectTimeout = ProviderNumber(
            configuration.ConnectTimeout.Value, 1, 3_600,
            $"{provider.Provider.Value} connect timeout seconds");
        NumericUpDown requestTimeout = ProviderNumber(
            configuration.RequestTimeout.Value, 1, 3_600,
            $"{provider.Provider.Value} request timeout seconds");
        TextBox? secretName = configuration.Kind is AgentModelProviderKind.OpenRouter
            ? ProviderTextBox(configuration.SecretName?.Value ?? string.Empty,
                $"{provider.Provider.Value} Secret Service key name")
            : null;
        TextBox? environmentVariable = configuration.Kind is AgentModelProviderKind.OpenRouter
            ? ProviderTextBox(configuration.EnvironmentVariable?.Value ?? string.Empty,
                $"{provider.Provider.Value} API key environment variable")
            : null;
        Button save = new()
        {
            Content = "Save provider configuration",
            IsEnabled = !settingsState.IsBusy,
        };
        save.Classes.Add("command");
        AutomationProperties.SetName(save, $"Save {provider.Provider.Value} provider configuration");
        save.Click += async (_, _) => await store.UpdateModelProviderAsync(new(
            configuration.Provider,
            new(endpoint.Text ?? string.Empty),
            new(chatModel.Text ?? string.Empty),
            new(embeddingModel.Text ?? string.Empty),
            new(decimal.ToInt32(dimensions.Value ?? 0)),
            new(decimal.ToInt32(connectTimeout.Value ?? 0)),
            new(decimal.ToInt32(requestTimeout.Value ?? 0)),
            secretName is null ? null : new(secretName.Text ?? string.Empty),
            environmentVariable is null || string.IsNullOrWhiteSpace(environmentVariable.Text)
                ? null
                : new(environmentVariable.Text)), cancellationToken);

        Grid fields = new()
        {
            ColumnDefinitions = new("*,*"),
            RowDefinitions = new("Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        AddProviderField(fields, 0, 0, "Endpoint", endpoint);
        AddProviderField(fields, 0, 1, "Default chat model", chatModel);
        AddProviderField(fields, 1, 0, "Default embedding model", embeddingModel);
        AddProviderField(fields, 1, 1, "Embedding dimensions", dimensions);
        AddProviderField(fields, 2, 0, "Connect timeout (seconds)", connectTimeout);
        AddProviderField(fields, 2, 1, "Request timeout (seconds)", requestTimeout);
        content.Children.Add(new Separator());
        content.Children.Add(fields);

        if (secretName is not null && environmentVariable is not null)
        {
            Grid secretReferences = new()
            {
                ColumnDefinitions = new("*,*"),
                ColumnSpacing = 10,
            };
            AddProviderField(secretReferences, 0, 0, "Secret Service key", secretName);
            AddProviderField(secretReferences, 0, 1, "Environment fallback", environmentVariable);
            content.Children.Add(secretReferences);

            TextBox credential = new()
            {
                PasswordChar = '●',
                PlaceholderText = "Paste a new API key",
                MinWidth = 280,
                IsEnabled = !settingsState.IsBusy,
            };
            AutomationProperties.SetName(credential, $"{provider.Provider.Value} API key");
            Button saveCredential = new()
            {
                Content = configuration.CredentialState is ModelProviderCredentialState.Configured
                    ? "Replace API key"
                    : "Save API key",
                IsEnabled = false,
            };
            saveCredential.Classes.Add("command");
            AutomationProperties.SetName(saveCredential, $"Save {provider.Provider.Value} API key");
            credential.GetObservable(TextBox.TextProperty).Subscribe(value =>
                saveCredential.IsEnabled = !settingsState.IsBusy && !string.IsNullOrWhiteSpace(value));
            saveCredential.Click += async (_, _) =>
            {
                await store.SetModelProviderCredentialAsync(new(
                    configuration.Provider,
                    new(credential.Text ?? string.Empty)), cancellationToken);
                credential.Text = string.Empty;
            };
            Grid credentialRow = new()
            {
                ColumnDefinitions = new("*,Auto"),
                ColumnSpacing = 10,
                Children = { credential },
            };
            Grid.SetColumn(saveCredential, 1);
            credentialRow.Children.Add(saveCredential);
            content.Children.Add(new TextBlock
            {
                Text = $"Credential: {configuration.CredentialState}" +
                       (configuration.CredentialMessage is null
                           ? string.Empty
                           : $" · {configuration.CredentialMessage}"),
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(credentialRow);
            content.Children.Add(new TextBlock
            {
                Text = "The key is write-only, stored in Linux Secret Service, and never saved in XML or application state. An environment value takes precedence when configured.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        content.Children.Add(save);
        content.Children.Add(new TextBlock
        {
            Text = configuration.RequiresRestart
                ? "Restart required: saved configuration differs from this running process."
                : "Endpoint, model, dimension, timeout, and secret-reference changes apply after restart.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
        });
        return new Border { Classes = { "card", "row" }, Child = content };
    }

    private static TextBox ProviderTextBox(string value, string accessibleName)
    {
        TextBox field = new() { Text = value, MinWidth = 220 };
        AutomationProperties.SetName(field, accessibleName);
        return field;
    }

    private NumericUpDown ProviderNumber(
        int value,
        int minimum,
        int maximum,
        string accessibleName)
    {
        NumericUpDown field = new()
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(field, accessibleName);
        return field;
    }

    private static void AddProviderField(
        Grid grid,
        int row,
        int column,
        string label,
        Control field)
    {
        StackPanel container = new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
                field,
            },
        };
        Grid.SetRow(container, row);
        Grid.SetColumn(container, column);
        grid.Children.Add(container);
    }

    private static Control PlannedPage(SettingsCategory category) => Page(
        category.Name,
        category.Summary,
        new Border
        {
            Classes = { "card" },
            Child = new TextBlock
            {
                Text = "No preferences from this category are exposed in this build yet.",
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

    private sealed record RemoteSpendChoice(RemoteSpendMode Mode, string Name)
    {
        public override string ToString() => Name;
    }

    private static bool TryParseUsd(string? text, out MicroUsdAmount? amount)
    {
        amount = null;
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out decimal usd) || usd <= 0)
        {
            return false;
        }

        decimal microUsd = usd * 1_000_000m;
        if (microUsd != decimal.Truncate(microUsd) || microUsd >= long.MaxValue)
        {
            return false;
        }

        amount = new((long)microUsd);
        return true;
    }

}
