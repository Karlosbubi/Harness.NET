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

internal enum SettingsCategoryId
{
    General,
    Editor,
    Keybindings,
    Appearance,
    ModelProviders,
    McpConnections,
    InboundMcp,
    DocumentationAndDependencies,
    AgentTools,
    VisualVerification,
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
            ["font", "code", "roslyn", "completion", "diagnostics", "inlay", "codelens", "references", "tests"], IsAvailable: true),
        new(SettingsCategoryId.Keybindings, "Keybindings", "Keyboard shortcuts and command discovery",
            ["keyboard", "shortcut", "keys", "bindings", "conflict", "reset", "import", "export", "command palette", "vim", "modal", "normal", "insert", "visual"], IsAvailable: true),
        new(SettingsCategoryId.Appearance, "Appearance & accessibility", "Theme and visual preferences",
            ["color", "theme", "contrast", "accessibility"], IsAvailable: true),
        new(SettingsCategoryId.ModelProviders, "Model providers", "Ollama and OpenRouter availability",
            ["model", "provider", "ollama", "openrouter", "remote", "local", "pricing"], IsAvailable: true),
        new(SettingsCategoryId.McpConnections, "MCP connections", "Stateless external tools and discovery",
            ["mcp", "model context protocol", "tool", "documentation", "stateless", "streamable http"], IsAvailable: true),
        new(SettingsCategoryId.InboundMcp, "Harness control", "Authenticated local MCP server and evaluation",
            ["inbound", "mcp", "control", "evaluation", "dogfood", "token", "client", "loopback"], IsAvailable: true),
        new(SettingsCategoryId.DocumentationAndDependencies, "Documentation & dependencies",
            "Versioned lookup, package evidence, cache, and SBOM",
            ["documentation", "research", "nuget", "package", "dependency", "sbom", "cyclonedx", "cache", "offline", "license", "advisory"], IsAvailable: true),
        new(SettingsCategoryId.AgentTools, "Agent tools", "Built-in IDE capabilities and authority",
            ["agent", "tool", "roslyn", "diagnostics", "symbol", "definition", "references", "authority", "on-demand"], IsAvailable: true),
        new(SettingsCategoryId.VisualVerification, "Visual verification", "Portal capture, privacy, retention, and evidence",
            ["screenshot", "screen capture", "xdg", "portal", "privacy", "retention", "size", "remote access", "agent", "visual"], IsAvailable: true),
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
    private static readonly string[] DefaultHarnessControlTools =
    [
        "harness_application", "harness_workspace", "harness_tree",
        "harness_read_range", "harness_git", "harness_project_graph",
        "harness_goals", "harness_evidence", "harness_workflow_evidence",
        "harness_goal_models",
        "harness_commit_preview", "harness_code_problems", "harness_code_symbol",
        "harness_code_definition", "harness_code_references",
        "harness_code_implementations", "harness_code_inspection", "harness_code_actions",
        "harness_create_goal",
        "harness_configure_goal", "harness_extend_goal_budget",
        "harness_select_goal_model", "harness_start_planning", "harness_resume_goal",
        "harness_retry_goal", "harness_cancel_goal_operation", "harness_abort_goal",
        "harness_decide_plan", "harness_request_commit", "harness_decide_commit",
        "harness_build", "harness_test",
    ];

    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly TextBox search = new();
    private readonly ListBox categories = new();
    private readonly ContentControl page = new();
    private AppearanceSnapshot? appearance;
    private ApplicationSettingsState settingsState = ApplicationSettingsState.Initial;
    private bool suppressThemeSelection;
    private string keybindingDocumentText = string.Empty;

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
        Button close = new() { Content = "Close" };
        close.Classes.Add("command");
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => Close();

        StackPanel navigation = new()
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "SETTINGS", Classes = { "eyebrow" } },
                search,
                categories,
                close,
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
            SettingsCategoryId.Editor or
            SettingsCategoryId.McpConnections or SettingsCategoryId.ModelsAndRoles or
            SettingsCategoryId.InboundMcp or
        SettingsCategoryId.DocumentationAndDependencies or
            SettingsCategoryId.PrivacyAndLimits or SettingsCategoryId.AgentTools or
            SettingsCategoryId.VisualVerification)
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
            SettingsCategoryId.Editor => EditorPage(),
            SettingsCategoryId.Keybindings => KeybindingsPage(),
            SettingsCategoryId.ModelProviders => ModelProvidersPage(),
            SettingsCategoryId.McpConnections => McpConnectionsPage(),
            SettingsCategoryId.InboundMcp => InboundMcpPage(),
            SettingsCategoryId.DocumentationAndDependencies => DocumentationAndDependenciesPage(),
            SettingsCategoryId.AgentTools => AgentToolsPage(),
            SettingsCategoryId.VisualVerification => VisualVerificationPage(),
            SettingsCategoryId.ModelsAndRoles => ModelsAndRolesPage(),
            SettingsCategoryId.PrivacyAndLimits => PrivacyAndLimitsPage(),
            _ => PlannedPage(category),
        };
    }

    private Control EditorPage()
    {
        EditorIntelligencePreferences current = settingsState.EditorIntelligenceSettings?
            .Preferences ?? EditorIntelligencePreferences.Default;
        CheckBox parameterNames = new()
        {
            Content = "Show parameter-name hints",
            IsChecked = current.ShowParameterNameHints,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(parameterNames, "Show Roslyn parameter name inlay hints");
        CheckBox inferredTypes = new()
        {
            Content = "Show inferred types for var and implicit parameters",
            IsChecked = current.ShowInferredTypeHints,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(inferredTypes, "Show Roslyn inferred type inlay hints");
        CheckBox references = new()
        {
            Content = "Show Find references CodeLens",
            IsChecked = current.ShowReferenceCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(references, "Show reference CodeLens actions");
        CheckBox implementations = new()
        {
            Content = "Show Find implementations CodeLens when applicable",
            IsChecked = current.ShowImplementationCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(implementations, "Show implementation CodeLens actions");
        CheckBox tests = new()
        {
            Content = "Show Find tests CodeLens for types and methods",
            IsChecked = current.ShowTestCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(tests, "Show associated test CodeLens actions");
        CheckBox run = new()
        {
            Content = "Show Run CodeLens for valid project entry points",
            IsChecked = current.ShowRunCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(run, "Show project Run CodeLens actions");
        CheckBox debug = new()
        {
            Content = "Show Debug CodeLens when a debugger is available",
            IsChecked = current.ShowDebugCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(debug, "Show project Debug CodeLens actions");
        CheckBox formatOnPaste = new()
        {
            Content = "Format pasted C# code with Roslyn",
            IsChecked = current.FormatOnPaste,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(formatOnPaste, "Format C# code on paste");
        CheckBox formatOnType = new()
        {
            Content = "Format after ;, }, or a new line",
            IsChecked = current.FormatOnType,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(formatOnType, "Format C# code on supported typing triggers");
        Button save = new()
        {
            Content = "Save editor settings",
            IsEnabled = !settingsState.IsBusy,
        };
        save.Classes.Add("primary");
        AutomationProperties.SetName(save, "Save editor intelligence settings");
        save.Click += async (_, _) => await store.SaveEditorIntelligenceSettingsAsync(new(
            parameterNames.IsChecked is true,
            inferredTypes.IsChecked is true,
            references.IsChecked is true,
            implementations.IsChecked is true,
            tests.IsChecked is true,
            formatOnPaste.IsChecked is true,
            formatOnType.IsChecked is true,
            run.IsChecked is true,
            debug.IsChecked is true), cancellationToken);

        return Page(
            "Editor",
            "Choose exact-buffer Roslyn hints, formatting, and lazy navigation for trusted C# editors.",
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Inlay hints", FontWeight = FontWeight.SemiBold },
                    parameterNames,
                    inferredTypes,
                    new TextBlock
                    {
                        Text = "Hints are computed only for the visible live buffer and never change source text.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock { Text = "Formatting", FontWeight = FontWeight.SemiBold },
                    formatOnPaste,
                    formatOnType,
                    new TextBlock
                    {
                        Text = "Automatic formatting is cancelled when the buffer changes, produces one undoable edit, and never saves the file.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock { Text = "CodeLens", FontWeight = FontWeight.SemiBold },
                    references,
                    implementations,
                    tests,
                    run,
                    debug,
                    new TextBlock
                    {
                        Text = "CodeLens actions resolve only when selected. Run and Debug appear only after a valid typed execution target is available.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    save,
                    new TextBlock
                    {
                        Text = settingsState.EditorIntelligenceSettings?.Status ??
                               "Editor settings are loading.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
    }

    private Control KeybindingsPage()
    {
        KeybindingSettingsSnapshot snapshot = settingsState.KeybindingSettings ??
                                               KeybindingSettingsSnapshot.Default;
        ComboBox inputMode = new()
        {
            ItemsSource = Enum.GetValues<EditorInputMode>(),
            SelectedItem = snapshot.InputMode,
            IsEnabled = !settingsState.IsBusy,
            MinWidth = 220,
        };
        AutomationProperties.SetName(inputMode, "Editor keyboard input mode");
        Dictionary<KeybindingCommand, TextBox> editors = [];
        TextBlock validation = new()
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
        };
        Button save = new()
        {
            Content = "Save keybindings",
            IsEnabled = !settingsState.IsBusy,
            Classes = { "primary" },
        };
        AutomationProperties.SetName(save, "Save validated keybindings");

        StackPanel rows = new() { Spacing = 10 };
        foreach (KeybindingCommandBindings binding in snapshot.Bindings)
        {
            TextBox editor = new()
            {
                Text = binding.DisplayText,
                PlaceholderText = "Unbound",
                IsEnabled = !settingsState.IsBusy,
            };
            AutomationProperties.SetName(editor, $"Shortcut for {binding.Definition.Title}");
            editors.Add(binding.Definition.Command, editor);
            Grid row = new()
            {
                ColumnDefinitions = new("240,*"),
                ColumnSpacing = 12,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = binding.Definition.Title,
                                FontWeight = FontWeight.SemiBold,
                            },
                            new TextBlock
                            {
                                Text = binding.Definition.Category,
                                Classes = { "muted" },
                                FontSize = 11,
                            },
                        },
                    },
                },
            };
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            rows.Children.Add(row);
        }

        KeybindingUpdateRequest Draft() => new(editors.Select(pair =>
                new KeybindingUpdateEntry(pair.Key, pair.Value.Text ?? string.Empty)).ToArray(),
            inputMode.SelectedItem is EditorInputMode selected
                ? selected
                : EditorInputMode.Standard);
        void ValidateDraft()
        {
            KeybindingValidationResult result = store.ValidateKeybindings(Draft());
            save.IsEnabled = result.IsValid && !settingsState.IsBusy;
            validation.Text = result.IsValid
                ? "No conflicts. Changes take effect immediately after saving."
                : string.Join('\n', result.Issues.Select(issue => $"• {issue.Message}").Distinct());
            validation.Classes.Set("warning", !result.IsValid);
        }
        foreach (TextBox editor in editors.Values)
        {
            editor.GetObservable(TextBox.TextProperty).Subscribe(_ => ValidateDraft());
        }
        inputMode.SelectionChanged += (_, _) => ValidateDraft();
        save.Click += async (_, _) =>
        {
            await store.SaveKeybindingsAsync(Draft(), cancellationToken);
            validation.Text = store.Current.Settings.Status ?? validation.Text;
        };

        Button reset = new() { Content = "Reset to defaults", IsEnabled = !settingsState.IsBusy };
        reset.Classes.Add("command");
        AutomationProperties.SetName(reset, "Reset all keybindings to defaults");
        reset.Click += async (_, _) =>
        {
            await store.ResetKeybindingsAsync(cancellationToken);
            RenderSelectedPage();
        };

        TextBox document = new()
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 150,
            MaxHeight = 260,
            PlaceholderText = "Versioned keybinding JSON appears here",
            IsEnabled = !settingsState.IsBusy,
            Text = keybindingDocumentText,
        };
        document.GetObservable(TextBox.TextProperty).Subscribe(value =>
            keybindingDocumentText = value ?? string.Empty);
        AutomationProperties.SetName(document, "Keybinding import and export document");
        Button export = new() { Content = "Export and copy JSON", IsEnabled = !settingsState.IsBusy };
        export.Classes.Add("command");
        AutomationProperties.SetName(export, "Export keybindings as safe JSON");
        export.Click += async (_, _) =>
        {
            string? text = await store.ExportKeybindingsAsync(cancellationToken);
            if (text is null) return;
            keybindingDocumentText = text;
            document.Text = text;
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        Button import = new() { Content = "Validate and import JSON", IsEnabled = !settingsState.IsBusy };
        import.Classes.Add("command");
        AutomationProperties.SetName(import, "Validate and import keybinding JSON");
        import.Click += async (_, _) =>
        {
            await store.ImportKeybindingsAsync(document.Text ?? string.Empty, cancellationToken);
            validation.Text = store.Current.Settings.Status ?? validation.Text;
        };
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { save, reset },
        };
        StackPanel transfer = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { export, import },
        };

        ValidateDraft();
        return Page(
            "Keybindings",
            "Configure real workbench commands. Separate alternate gestures with a semicolon.",
            new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new Border
                    {
                        Classes = { "card" },
                        Child = new TextBlock
                        {
                            Text = "Reserved desktop, accessibility, and unmodified typing keys cannot be assigned. Conflicts block saving instead of choosing an arbitrary command.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    Labeled("Editor input mode", inputMode,
                        "Vim starts each source editor in Normal mode. Escape or Ctrl+[ leaves Insert or Visual mode after IME composition ends. Application shortcuts remain active."),
                    rows,
                    validation,
                    actions,
                    new Separator(),
                    new TextBlock { Text = "Portable configuration", FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = "Import accepts only the bounded harness-keybindings-v1 JSON schema. It cannot name files, scripts, or executable actions.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    document,
                    transfer,
                    new TextBlock
                    {
                        Text = settingsState.KeybindingSettings?.Status ??
                               "Keybinding settings are loading; safe defaults are shown.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
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
            if (model.SelectedCandidate is { } candidate)
            {
                await store.UpdateAgentDefaultAsync(
                    roleDefault.Role,
                    candidate,
                    cancellationToken);
            }
        };

        Border unavailable = new()
        {
            Classes = { "editor-access" },
            IsVisible = choices.Length == 0,
            Child = new TextBlock
            {
                Text = "Discover available models to edit this route.",
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Grid fields = new()
        {
            RowDefinitions = new("Auto,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 10,
            Children = { model, unavailable },
        };
        Grid.SetRow(save, 1);
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
                            ? $"Effective: {roleDefault.Access} · {roleDefault.Provider.Value}/{roleDefault.Model.Value}" +
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

    private Control InboundMcpPage()
    {
        InboundMcpSettingsView? snapshot = settingsState.InboundMcpSettings;
        InboundControlSettings configured = snapshot?.Settings ?? new(
            false, InboundControlMode.Normal, new Uri("http://127.0.0.1:57431/mcp"), [],
            [new("harness_application"), new("harness_workspace"), new("harness_tree"),
                new("harness_read_range"), new("harness_git"), new("harness_project_graph"),
                new("harness_goals"), new("harness_evidence"),
                new("harness_workflow_evidence"), new("harness_goal_models"),
                new("harness_commit_preview"), new("harness_ui"),
                new("harness_open_document"), new("harness_audit"),
                new("harness_evaluation_snapshot"),
                new("harness_code_problems"), new("harness_code_symbol"),
                new("harness_code_definition"), new("harness_code_references"),
                new("harness_code_implementations"), new("harness_code_inspection"),
                new("harness_code_actions")],
            [], TimeSpan.FromSeconds(30), 500, 1_000, false);

        CheckBox enabled = new()
        {
            Content = "Enable authenticated loopback MCP server",
            IsChecked = configured.IsEnabled
        };
        AutomationProperties.SetName(enabled, "Enable inbound MCP server");
        ComboBox mode = new()
        {
            ItemsSource = Enum.GetValues<InboundControlMode>(),
            SelectedItem = configured.Mode,
            MinWidth = 260,
        };
        AutomationProperties.SetName(mode, "Inbound MCP mode");
        TextBox endpoint = new() { Text = configured.Endpoint.AbsoluteUri };
        AutomationProperties.SetName(endpoint, "Inbound MCP loopback endpoint");
        TextBox clients = new()
        {
            Text = string.Join(Environment.NewLine, configured.AllowedClients.Select(item => item.Value)),
            AcceptsReturn = true,
            Height = 70,
            PlaceholderText = "One allowed client ID per line; empty allows any authenticated client",
        };
        AutomationProperties.SetName(clients, "Allowed inbound MCP client IDs");
        TextBox tools = new()
        {
            Text = string.Join(Environment.NewLine, configured.AllowedTools.Select(item => item.Value)),
            AcceptsReturn = true,
            Height = 110,
        };
        AutomationProperties.SetName(tools, "Allowed inbound MCP tool IDs");
        TextBox approvals = new()
        {
            Text = string.Join(Environment.NewLine,
                configured.ApprovalRequiredTools.Select(item => item.Value)),
            AcceptsReturn = true,
            Height = 70,
            PlaceholderText = "One allowed tool ID per line",
        };
        AutomationProperties.SetName(approvals,
            "Inbound MCP tool IDs requiring explicit approval");
        NumericUpDown timeout = new()
        {
            Minimum = 1,
            Maximum = 300,
            Value = (decimal)configured.RequestTimeout.TotalSeconds
        };
        AutomationProperties.SetName(timeout, "Inbound MCP request timeout in seconds");
        NumericUpDown resultLimit = new()
        {
            Minimum = 1,
            Maximum = 5000,
            Value = configured.ResultLimit
        };
        AutomationProperties.SetName(resultLimit, "Inbound MCP result limit");
        NumericUpDown retention = new()
        {
            Minimum = 0,
            Maximum = 100000,
            Value = configured.AuditRetention
        };
        AutomationProperties.SetName(retention, "Inbound MCP audit retention");
        string policySummary = snapshot?.ToolPolicies.Count > 0
            ? string.Join(Environment.NewLine, snapshot.ToolPolicies.Select(policy =>
                $"{policy.Id.Value} · " +
                (policy.IsReadOnly ? "read" : "mutation") +
                (policy.IsExecution ? " · execution" : string.Empty) +
                (policy.IsSensitive ? " · sensitive" : string.Empty) +
                (policy.IsDestructive ? " · destructive" : string.Empty) +
                (policy.IsIdempotent ? " · idempotent" : string.Empty)))
            : "Tool policy catalog unavailable.";

        Button save = new() { Content = "Apply server settings" };
        save.Classes.Add("primary");
        save.IsEnabled = !settingsState.IsBusy;
        save.Click += async (_, _) =>
        {
            if (!Uri.TryCreate(endpoint.Text?.Trim(), UriKind.Absolute, out Uri? parsed)) return;
            await store.SaveInboundMcpAsync(configured with
            {
                IsEnabled = enabled.IsChecked == true,
                Mode = mode.SelectedItem is InboundControlMode selected ? selected : InboundControlMode.Normal,
                Endpoint = parsed,
                AllowedClients = Lines(clients.Text).Select(value => new InboundControlClientId(value)).ToArray(),
                AllowedTools = Lines(tools.Text).Select(value => new InboundControlToolId(value)).ToArray(),
                ApprovalRequiredTools = Lines(approvals.Text)
                    .Select(value => new InboundControlToolId(value)).ToArray(),
                RequestTimeout = TimeSpan.FromSeconds((double)(timeout.Value ?? 30)),
                ResultLimit = (int)(resultLimit.Value ?? 500),
                AuditRetention = (int)(retention.Value ?? 1000),
            }, cancellationToken);
        };
        Button rotate = new() { Content = "Rotate and copy token once" };
        rotate.Click += async (_, _) =>
        {
            string? token = await store.RotateInboundMcpTokenAsync(cancellationToken);
            if (token is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(token);
        };
        Button resetEvaluation = new()
        {
            Content = "Reset isolated fixture",
            IsEnabled = configured.Mode is InboundControlMode.IsolatedEvaluation &&
                !settingsState.IsBusy,
        };
        resetEvaluation.Click += async (_, _) =>
            await store.ResetInboundMcpEvaluationAsync(cancellationToken);

        InboundControlStatus? status = snapshot?.Status;
        StackPanel activeClients = new() { Spacing = 6 };
        foreach (InboundControlClientStatus client in status?.ActiveClients ?? [])
        {
            Button disconnect = new() { Content = $"Disconnect {client.Id.Value}" };
            disconnect.Click += async (_, _) => await store.DisconnectInboundMcpClientAsync(
                client.Id, cancellationToken);
            activeClients.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"{client.Id.Value} · {client.RequestCount} request(s) · {client.LastSeenAt:O}",
                        VerticalAlignment = VerticalAlignment.Center },
                    disconnect,
                },
            });
        }
        if (activeClients.Children.Count == 0)
            activeClients.Children.Add(new TextBlock { Text = "No authenticated clients.", Classes = { "muted" } });

        return Page(
            "Harness control",
            "Expose typed Harness.NET inspection to local MCP clients. Authentication never replaces workspace trust, baselines, approvals, capture consent, or execution policy.",
            new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = status is null ? "Runtime status unavailable." :
                            $"{(status.IsRunning ? "ACTIVE" : "INACTIVE")} · {status.Mode} · instance {status.InstanceId}\n" +
                            $"{status.Endpoint} · authentication {(status.IsAuthenticated ? "ready" : "not ready")}" +
                            (status.Error is null ? string.Empty : $"\n{status.Error}"),
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { status?.IsRunning == true ? "status-success" : "muted" },
                    },
                    enabled,
                    Labeled("Mode", mode, "Evaluation mode requires a dedicated temporary root supplied at process startup."),
                    Labeled("Loopback endpoint", endpoint, "Plain HTTP is accepted only on 127.0.0.1, ::1, or localhost."),
                    Labeled("Allowed clients", clients, "Clients also need the current bearer token and X-Harness-Client header."),
                    Labeled("Allowed tools", tools, "Closed typed tool IDs only. Unknown IDs expose nothing."),
                    Labeled("Require explicit approval", approvals,
                        "These tools stay out of discovery until the developer removes the approval requirement and applies settings."),
                    new Border { Classes = { "card" }, Child = new TextBlock
                    {
                        Text = "Closed tool catalog\n" + policySummary,
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    } },
                    Labeled("Request timeout (seconds)", timeout),
                    Labeled("Maximum results", resultLimit),
                    Labeled("Audit records retained", retention),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { save, rotate, resetEvaluation } },
                    new TextBlock { Text = "Active clients", FontWeight = FontWeight.SemiBold },
                    activeClients,
                    new TextBlock
                    {
                        Text = "Never exposed: generic shell, SQL, click/type, coordinates, desktop control, silent screenshots, secrets, or arbitrary command dispatch.",
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    },
                },
            });
    }

    private static Control Labeled(string label, Control control, string? help = null)
    {
        StackPanel field = new() { Spacing = 4 };
        field.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
        field.Children.Add(control);
        if (help is not null) field.Children.Add(new TextBlock
        { Text = help, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        return field;
    }

    private Control McpConnectionsPage()
    {
        Button refresh = new()
        {
            Content = "Refresh active connections",
            IsEnabled = !settingsState.IsBusy,
        };
        refresh.Classes.Add("command");
        AutomationProperties.SetName(refresh, "Refresh stateless MCP connections");
        refresh.Click += async (_, _) => await store.RefreshMcpAsync(cancellationToken);

        TextBox name = ProviderTextBox(string.Empty, "New MCP connection name");
        name.PlaceholderText = "docs";
        TextBox endpoint = ProviderTextBox(string.Empty, "New MCP endpoint");
        endpoint.PlaceholderText = "https://example.test/mcp";
        NumericUpDown timeout = ProviderNumber(30, 1, 3_600, "New MCP request timeout seconds");
        ComboBox kind = new()
        {
            ItemsSource = Enum.GetValues<McpConnectionKind>(),
            SelectedItem = McpConnectionKind.ReadOnly,
            MinWidth = 220,
        };
        AutomationProperties.SetName(kind, "New MCP connection kind");
        TextBox clientId = ProviderTextBox("harness-controller", "New Harness control client ID");
        TextBox bearerToken = new()
        {
            PasswordChar = '●',
            PlaceholderText = "Paste worker bearer token (write-only)",
        };
        AutomationProperties.SetName(bearerToken, "New Harness control bearer token");
        TextBox allowedTools = new()
        {
            Text = string.Empty,
            AcceptsReturn = true,
            Height = 100,
            PlaceholderText = "One exact harness_ tool ID per line",
        };
        AutomationProperties.SetName(allowedTools, "New Harness control allowed tool IDs");
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is McpConnectionKind.HarnessControl &&
                string.IsNullOrWhiteSpace(allowedTools.Text))
            {
                allowedTools.Text = string.Join(
                    Environment.NewLine, DefaultHarnessControlTools);
            }
        };
        CheckBox enabled = new() { Content = "Enable after restart", IsChecked = true };
        AutomationProperties.SetName(enabled, "Enable new MCP connection after restart");
        Button add = new() { Content = "Add connection", IsEnabled = !settingsState.IsBusy };
        add.Classes.Add("accent");
        AutomationProperties.SetName(add, "Add stateless MCP connection");
        add.Click += async (_, _) =>
        {
            await store.SaveMcpConnectionAsync(new(
                new(name.Text ?? string.Empty),
                new(endpoint.Text ?? string.Empty),
                new(decimal.ToInt32(timeout.Value ?? 30)),
                (McpConnectionKind)(kind.SelectedItem ?? McpConnectionKind.ReadOnly),
                kind.SelectedItem is McpConnectionKind.HarnessControl
                    ? new(clientId.Text ?? string.Empty)
                    : null,
                kind.SelectedItem is McpConnectionKind.HarnessControl &&
                    !string.IsNullOrWhiteSpace(bearerToken.Text)
                    ? new(bearerToken.Text)
                    : null,
                kind.SelectedItem is McpConnectionKind.HarnessControl
                    ? Lines(allowedTools.Text).Select(tool =>
                        new McpAllowedToolName(tool)).ToArray()
                    : [],
                enabled.IsChecked == true), cancellationToken);
            if (store.Current.Settings.Status?.StartsWith(
                    "MCP connection saved", StringComparison.Ordinal) == true)
            {
                name.Text = string.Empty;
                endpoint.Text = string.Empty;
                bearerToken.Text = string.Empty;
                allowedTools.Text = string.Empty;
            }
        };

        Grid newFields = new()
        {
            ColumnDefinitions = new("*,*"),
            RowDefinitions = new("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        AddProviderField(newFields, 0, 0, "Connection name", name);
        AddProviderField(newFields, 0, 1, "Streamable HTTP endpoint", endpoint);
        AddProviderField(newFields, 1, 0, "Request timeout (seconds)", timeout);
        AddProviderField(newFields, 1, 1, "Connection kind", kind);
        AddProviderField(newFields, 2, 0, "Harness control client ID", clientId);
        AddProviderField(newFields, 2, 1, "Harness control bearer token", bearerToken);
        AddProviderField(newFields, 3, 0, "Harness control allowed tools", allowedTools);
        Grid.SetRow(enabled, 3);
        Grid.SetColumn(enabled, 1);
        newFields.Children.Add(enabled);
        Grid.SetRow(add, 4);
        newFields.Children.Add(add);

        StackPanel connections = new() { Spacing = 12 };
        foreach (McpConnectionSettingsView connection in
                 settingsState.McpSettings?.Connections ?? [])
        {
            connections.Children.Add(McpConnectionCard(connection));
        }

        if (connections.Children.Count == 0)
        {
            connections.Children.Add(new TextBlock
            {
                Text = "No MCP connections are configured. Add a stateless Streamable HTTP endpoint above.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return Page(
            "MCP connections",
            "Manage first-class Model Context Protocol endpoints. Ordinary connections expose only explicitly read-only, non-destructive tools. Harness control is a separate loopback-only, exactly allowlisted Lead delegation mode.",
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
                            Text = "Read-only connections fail closed on missing or unsafe annotations. Harness control additionally requires a Harness.NET server identity, stable client ID, write-only Secret Service bearer token, and exact harness_ tool allowlist. It is exposed only to Lead. Saved changes require restart.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    new Border
                    {
                        Classes = { "card", "row" },
                        Child = new StackPanel
                        {
                            Spacing = 10,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "Add stateless connection",
                                    FontSize = 16,
                                    FontWeight = FontWeight.SemiBold,
                                },
                                newFields,
                            },
                        },
                    },
                    refresh,
                    connections,
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
    }

    private Control DocumentationAndDependenciesPage()
    {
        ResearchSettingsSnapshot? snapshot = settingsState.ResearchSettings;
        CheckBox exactLocal = new()
        {
            Content = "Search exact restored package and SDK documentation",
            IsChecked = snapshot?.ExactLocalEnabled ?? true,
        };
        CheckBox localIndex = new()
        {
            Content = "Search configured local documentation indexes",
            IsChecked = snapshot?.LocalIndexEnabled ?? true,
        };
        CheckBox mcp = new()
        {
            Content = "Use configured closed read-only MCP documentation tools",
            IsChecked = snapshot?.McpEnabled ?? true,
        };
        CheckBox web = new()
        {
            Content = "Use configured web search only when earlier evidence is insufficient",
            IsChecked = snapshot?.WebEnabled ?? true,
        };
        CheckBox offline = new()
        {
            Content = "Offline mode — local and cached evidence only",
            IsChecked = snapshot?.Offline ?? false,
        };
        TextBox indexRoots = Multiline(
            string.Join(Environment.NewLine, snapshot?.IndexRoots ?? []),
            "Documentation index roots, one absolute path per line", 82);
        TextBox mcpTools = Multiline(
            string.Join(Environment.NewLine, snapshot?.McpDocumentationTools ?? []),
            "MCP documentation tools, one connection/tool per line", 82);
        TextBox webEndpoints = Multiline(
            string.Join(Environment.NewLine, snapshot?.WebEndpoints ?? []),
            "Web documentation endpoints, one HTTPS URI per line", 82);
        TextBox packageSources = Multiline(
            string.Join(Environment.NewLine, snapshot?.PackageSources ??
                ["https://api.nuget.org/v3/index.json"]),
            "NuGet service indexes, one HTTPS URI per line", 82);
        ComboBox refresh = new()
        {
            ItemsSource = Enum.GetValues<ResearchRefreshMode>(),
            SelectedItem = snapshot?.RefreshMode ?? ResearchRefreshMode.OnDemand,
            MinWidth = 180,
        };
        AutomationProperties.SetName(refresh, "Documentation refresh policy");
        NumericUpDown maximumResults = ProviderNumber(
            snapshot?.MaximumResults ?? 5, 1, 20, "Maximum documentation results");
        NumericUpDown maximumCharacters = ProviderNumber(
            snapshot?.MaximumCharacters ?? 12_000, 1_000, 100_000,
            "Maximum documentation result characters");
        NumericUpDown cacheAge = ProviderNumber(
            snapshot?.MaximumCacheAgeHours ?? 168, 0, 8_760,
            "Maximum documentation cache age hours");
        NumericUpDown retention = ProviderNumber(
            snapshot?.RetentionDays ?? 30, 0, 3_650, "Documentation cache retention days");
        Button save = new() { Content = "Save documentation and dependency settings" };
        save.Classes.Add("accent");
        save.Click += async (_, _) => await store.SaveResearchSettingsAsync(new(
            exactLocal.IsChecked == true,
            localIndex.IsChecked == true,
            mcp.IsChecked == true,
            web.IsChecked == true,
            offline.IsChecked == true,
            Lines(indexRoots.Text),
            Lines(mcpTools.Text),
            Lines(webEndpoints.Text),
            Lines(packageSources.Text),
            refresh.SelectedItem is ResearchRefreshMode selectedRefresh
                ? selectedRefresh
                : ResearchRefreshMode.OnDemand,
            decimal.ToInt32(maximumResults.Value ?? 5),
            decimal.ToInt32(maximumCharacters.Value ?? 12_000),
            decimal.ToInt32(cacheAge.Value ?? 168),
            decimal.ToInt32(retention.Value ?? 30)), cancellationToken);
        Button cleanup = new() { Content = "Apply cache retention now" };
        cleanup.Classes.Add("command");
        cleanup.Click += async (_, _) => await store.CleanupResearchCacheAsync(cancellationToken);

        Grid limits = new()
        {
            ColumnDefinitions = new("*,*"),
            RowDefinitions = new("Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        AddProviderField(limits, 0, 0, "Refresh policy", refresh);
        AddProviderField(limits, 0, 1, "Maximum results", maximumResults);
        AddProviderField(limits, 1, 0, "Maximum result characters", maximumCharacters);
        AddProviderField(limits, 1, 1, "Maximum cache age (hours)", cacheAge);
        AddProviderField(limits, 2, 0, "Retention (days)", retention);

        TextBox library = ProviderTextBox("Avalonia", "Documentation library");
        TextBox version = ProviderTextBox(string.Empty, "Documentation library version");
        version.PlaceholderText = "Exact version (recommended)";
        TextBox question = Multiline(string.Empty, "Documentation question", 76);
        question.PlaceholderText = "What API or behavior do you need to verify?";
        Button lookup = new() { Content = "Look up documentation" };
        lookup.Classes.Add("accent");
        lookup.Click += async (_, _) => await store.LookupDocumentationAsync(
            library.Text ?? string.Empty, version.Text, question.Text ?? string.Empty,
            cancellationToken);
        StackPanel lookupEvidence = new() { Spacing = 8 };
        if (settingsState.DocumentationLookup is { } documentation)
        {
            lookupEvidence.Children.Add(new TextBlock
            {
                Text = $"Sufficient: {documentation.IsSufficient} · Conflicts: {documentation.HasConflicts} · " +
                       $"{documentation.Results.Count} result(s)",
                FontWeight = FontWeight.SemiBold,
            });
            foreach (DocumentationEvidenceView result in documentation.Results)
            {
                lookupEvidence.Children.Add(new Border
                {
                    Classes = { "card" },
                    Child = new TextBlock
                    {
                        Text = $"#{result.Rank} {result.Title}\n{result.Content}\n" +
                               $"Source: {result.Source.Value} ({result.SourceKind}) · " +
                               $"Version: {result.Version?.Value ?? "unknown"} · {result.Freshness} · " +
                               $"{result.Confidence}\nCitation: {result.Citation.Value}",
                        TextWrapping = TextWrapping.Wrap,
                    },
                });
            }
            if (documentation.Escalation.Count > 0)
            {
                lookupEvidence.Children.Add(new TextBlock
                {
                    Text = "Lookup path:\n" + string.Join("\n", documentation.Escalation.Select(item =>
                        $"{item.SourceKind}/{item.Source.Value}: {item.Action} — {item.Reason}")),
                    Classes = { "muted" },
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        Button inspect = new() { Content = "Inspect dependency graph" };
        inspect.Classes.Add("command");
        inspect.Click += async (_, _) => await store.InspectDependenciesAsync(cancellationToken);
        Button previewSbom = new() { Content = "Preview deterministic SBOM" };
        previewSbom.Classes.Add("command");
        previewSbom.Click += async (_, _) => await store.PreviewSbomAsync(cancellationToken);
        string dependencySummary = settingsState.DependencyInspection is not { } dependency
            ? "No dependency inspection has run. Inspection reads existing files and never restores."
            : dependency.Error ??
              $"{dependency.Projects.Count} project(s) · " +
              $"{dependency.Projects.Sum(project => project.Packages.Count)} package graph entries · " +
              $"{dependency.Conflicts.Count} conflict(s)";
        TextBox package = ProviderTextBox(string.Empty, "Candidate package ID");
        TextBox candidateVersion = ProviderTextBox(string.Empty, "Candidate exact package version");
        CheckBox allowPrerelease = new() { Content = "Allow prerelease candidate" };
        Button validate = new() { Content = "Validate exact candidate" };
        validate.Classes.Add("command");
        validate.Click += async (_, _) => await store.ValidatePackageCandidateAsync(
            package.Text ?? string.Empty, candidateVersion.Text ?? string.Empty,
            allowPrerelease.IsChecked == true, cancellationToken);
        Button previewChange = new() { Content = "Preview package + SBOM diff" };
        previewChange.Classes.Add("accent");
        previewChange.Click += async (_, _) => await store.PreviewPackageChangeAsync(
            package.Text ?? string.Empty, candidateVersion.Text ?? string.Empty,
            allowPrerelease.IsChecked == true, cancellationToken);
        string candidateSummary = settingsState.PackageCandidateValidation is not { } candidate
            ? "No package candidate has been validated."
            : $"{candidate.Decision}: {string.Join(" ", candidate.Findings)}";
        string changeDiff = settingsState.PackageChangePreview is not { } change
            ? string.Empty
            : change.Error ?? change.DependencyDiff + "\n" + change.SbomDiff;

        TextBox exportPath = ProviderTextBox(string.Empty, "SBOM export destination");
        exportPath.PlaceholderText = "/absolute/path/bom.json";
        CheckBox overwrite = new() { Content = "Overwrite existing destination" };
        Button export = new() { Content = "Export current SBOM…" };
        export.Classes.Add("command");
        export.Click += async (_, _) => await store.ExportSbomAsync(
            exportPath.Text ?? string.Empty, overwrite.IsChecked == true, cancellationToken);
        string sbomSummary = settingsState.SbomPreview?.Sbom is not { } sbom
            ? settingsState.SbomPreview?.Error ?? "No SBOM preview generated."
            : $"{sbom.Format} · SHA-256 {sbom.Sha256}\n{sbom.Json}";

        return Page(
            "Documentation & dependencies",
            "Use version-matched documentation only when needed. Inspect package and supply-chain evidence without a model, restore, or repository mutation. Unknown facts remain unknown.",
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
                            Text = "Lookup order is fixed: exact local/package docs → local index → configured MCP → web. Offline mode blocks live MCP, web, and package-registry requests. SBOM export happens only when you press Export.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    new Border
                    {
                        Classes = { "card", "row" },
                        Child = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock { Text = "Sources and cache", FontSize = 16, FontWeight = FontWeight.SemiBold },
                                exactLocal, localIndex, mcp, web, offline,
                                new TextBlock { Text = "Local index roots", FontWeight = FontWeight.SemiBold }, indexRoots,
                                new TextBlock { Text = "MCP documentation tools", FontWeight = FontWeight.SemiBold }, mcpTools,
                                new TextBlock { Text = "Web JSON search endpoints", FontWeight = FontWeight.SemiBold }, webEndpoints,
                                new TextBlock { Text = "NuGet v3 service indexes", FontWeight = FontWeight.SemiBold }, packageSources,
                                limits,
                                new TextBlock
                                {
                                    Text = snapshot is null ? "Research services unavailable." :
                                        $"Cache: {snapshot.CacheEntries} entries · {snapshot.CacheBytes:N0} bytes" +
                                        (snapshot.LastCacheFailure is null ? string.Empty : $" · Last failure: {snapshot.LastCacheFailure}"),
                                    Classes = { "muted" }, TextWrapping = TextWrapping.Wrap,
                                },
                                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cleanup } },
                            },
                        },
                    },
                    new Border
                    {
                        Classes = { "card", "row" },
                        Child = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock { Text = "On-demand documentation lookup", FontSize = 16, FontWeight = FontWeight.SemiBold },
                                library, version, question, lookup, lookupEvidence,
                            },
                        },
                    },
                    new Border
                    {
                        Classes = { "card", "row" },
                        Child = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock { Text = "Dependency evidence and package preview", FontSize = 16, FontWeight = FontWeight.SemiBold },
                                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { inspect, previewSbom } },
                                new TextBlock { Text = dependencySummary, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                                package, candidateVersion, allowPrerelease,
                                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { validate, previewChange } },
                                new TextBlock { Text = candidateSummary, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                                new TextBox { Text = changeDiff, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 240, TextWrapping = TextWrapping.NoWrap },
                            },
                        },
                    },
                    new Border
                    {
                        Classes = { "card", "row" },
                        Child = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock { Text = "SBOM preview and explicit export", FontSize = 16, FontWeight = FontWeight.SemiBold },
                                new TextBox { Text = sbomSummary, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 260, TextWrapping = TextWrapping.NoWrap },
                                exportPath, overwrite, export,
                            },
                        },
                    },
                    new TextBlock { Text = settingsState.Status ?? string.Empty, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                },
            });
    }

    private Control AgentToolsPage()
    {
        StackPanel modules = new() { Spacing = 12 };
        Dictionary<string, CheckBox> exposure = new(StringComparer.Ordinal);
        HashSet<string> direct = settingsState.AgentToolExposure?.DirectModules
            .Select(item => item.Value).ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (AgentToolModule module in AgentToolCatalog.Default.Modules)
        {
            string roles = string.Join(", ", module.Roles.Select(role => role.ToString()));
            string operations = module.Operations.Count == 0
                ? "No model schemas exposed"
                : string.Join(", ", module.Operations.Select(operation => operation.Value));
            string status = module.Availability is AgentToolModuleAvailability.Available
                ? "Available"
                : $"Planned · {module.UnavailableReason}";
            StackPanel card = new()
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = module.DisplayName,
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = module.Summary,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = $"{status}\nSource: {module.Source.Value} · Roles: {roles}\n" +
                               $"Exposure: {module.Exposure} · Authority: {module.Authority}\n" +
                               $"Mode: {(module.IsOptional ? "Optional" : "Required core")}\n" +
                               $"Operations: {operations}",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };
            if (module.IsOptional && module.Availability is AgentToolModuleAvailability.Available &&
                module.Exposure is AgentToolExposure.OnDemand)
            {
                CheckBox expose = new()
                {
                    Content = "Expose directly on every eligible role turn",
                    IsChecked = direct.Contains(module.Id.Value),
                };
                AutomationProperties.SetName(expose, $"Expose {module.DisplayName} directly");
                exposure[module.Id.Value] = expose;
                card.Children.Add(expose);
            }
            modules.Children.Add(new Border
            {
                Classes = { "card", "row" },
                Child = card,
            });
        }

        Button saveExposure = new() { Content = "Save optional exposure defaults" };
        saveExposure.Classes.Add("primary");
        saveExposure.Click += async (_, _) => await store.SaveAgentToolExposureAsync(
            exposure.Where(item => item.Value.IsChecked == true)
                .Select(item => new AgentToolModuleId(item.Key)).ToArray(), cancellationToken);
        modules.Children.Add(saveExposure);

        int externalConnections = settingsState.McpSettings?.Connections.Count ?? 0;
        modules.Children.Add(new Border
        {
            Classes = { "card" },
            Child = new TextBlock
            {
                Text = $"External MCP sources: {externalConnections} configured. " +
                       "Connection health and read-only eligibility are managed under MCP connections. " +
                       "External tools do not inherit built-in authority.",
                TextWrapping = TextWrapping.Wrap,
            },
        });

        return Page(
            "Agent tools",
            "See what models can use, where each capability comes from, and which authority boundary applies. On-demand requests grant schemas for one next role turn; saved direct exposure never bypasses operation approval.",
            modules);
    }

    private Control VisualVerificationPage()
    {
        VisualCaptureSettingsSnapshot? snapshot = settingsState.VisualCaptureSettings;
        VisualCapturePreferences preferences = snapshot?.Preferences ?? VisualCapturePreferences.Default;
        CheckBox enabled = new()
        {
            Content = "Allow consented single-frame capture",
            IsChecked = preferences.IsEnabled,
            IsEnabled = snapshot is not null && !settingsState.IsBusy,
        };
        AutomationProperties.SetName(enabled, "Enable visual verification capture");
        NumericUpDown maximumMiB = ProviderNumber(
            (int)(preferences.MaximumBytes.Value / (1024 * 1024)), 1, 16,
            "Maximum visual capture size in MiB");
        NumericUpDown retentionDays = ProviderNumber(
            preferences.RetentionDays.Value, 1, 90, "Visual capture retention days");
        NumericUpDown maximumPerGoal = ProviderNumber(
            preferences.MaximumPerGoal.Value, 1, 100, "Maximum visual captures per goal");
        CheckBox remote = new()
        {
            Content = "Allow remote models to receive captured images",
            IsChecked = preferences.AllowRemoteModelAccess,
            IsEnabled = snapshot is not null && !settingsState.IsBusy,
        };
        AutomationProperties.SetName(remote, "Allow remote model access to visual captures");
        Button save = new() { Content = "Save visual verification settings", IsEnabled = snapshot is not null && !settingsState.IsBusy };
        save.Classes.Add("accent");
        AutomationProperties.SetName(save, "Save visual verification settings");
        save.Click += async (_, _) => await store.SaveVisualCaptureSettingsAsync(new(
            enabled.IsChecked == true,
            new(decimal.ToInt64(maximumMiB.Value ?? 5) * 1024 * 1024),
            new(decimal.ToInt32(retentionDays.Value ?? 7)),
            new(decimal.ToInt32(maximumPerGoal.Value ?? 20)),
            remote.IsChecked == true), cancellationToken);

        ComboBox target = new()
        {
            ItemsSource = Enum.GetValues<VisualCaptureTarget>(),
            SelectedItem = VisualCaptureTarget.UserSelection,
            MinWidth = 220,
        };
        AutomationProperties.SetName(target, "Visual capture target");
        Button capture = new()
        {
            Content = "Capture one frame…",
            IsEnabled = snapshot is not null && !settingsState.IsBusy,
        };
        capture.Classes.Add("accent");
        AutomationProperties.SetName(capture, "Capture one visual verification frame");
        capture.Click += async (_, _) => await store.CaptureVisualAsync(
            target.SelectedItem is VisualCaptureTarget selected
                ? selected
                : VisualCaptureTarget.UserSelection,
            new VisualCaptureUiScale(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1),
            parentWindow: null,
            cancellationToken);
        Button refresh = new() { Content = "Refresh goal evidence" };
        refresh.Classes.Add("command");
        refresh.Click += async (_, _) => await store.RefreshVisualCapturesAsync(cancellationToken);

        ComboBox captures = new()
        {
            ItemsSource = settingsState.VisualCaptures.Select(item => new VisualCaptureChoice(item)).ToArray(),
            MinWidth = 360,
            PlaceholderText = "Select a stored frame",
        };
        AutomationProperties.SetName(captures, "Stored visual captures for selected goal");
        captures.SelectionChanged += async (_, _) =>
        {
            if (captures.SelectedItem is VisualCaptureChoice choice)
            {
                await store.InspectVisualCaptureAsync(choice.Capture.Id, cancellationToken);
            }
        };
        Button delete = new() { Content = "Delete selected frame", IsEnabled = settingsState.SelectedVisualCapture is not null };
        delete.Classes.Add("danger");
        AutomationProperties.SetName(delete, "Delete selected visual capture");
        delete.Click += async (_, _) =>
        {
            if (settingsState.SelectedVisualCapture is { } content)
            {
                await store.DeleteVisualCaptureAsync(content.Capture.Id, cancellationToken);
            }
        };

        StackPanel preview = new() { Spacing = 8 };
        if (settingsState.SelectedVisualCapture is { } selectedCapture)
        {
            byte[] bytes = Convert.FromBase64String(selectedCapture.Content.Base64);
            Image exactFrame = new()
            {
                Source = new Bitmap(new MemoryStream(bytes, writable: false)),
                MaxHeight = 360,
                Stretch = Stretch.Uniform,
            };
            AutomationProperties.SetName(exactFrame, "Exact stored visual capture frame");
            preview.Children.Add(exactFrame);
            VisualCaptureView item = selectedCapture.Capture;
            preview.Children.Add(new TextBlock
            {
                Text = $"Goal {item.GoalId.Value} · {item.CreatedAt.LocalDateTime:G}\n" +
                       $"Initiator: {item.Initiator} · Action: {item.RelatedAction.Value}\n" +
                       $"Application: {item.ApplicationIdentity.Value} · Target: {item.Target}\n" +
                       $"{item.PixelSize.Width}×{item.PixelSize.Height} · {item.Bytes.Value:N0} bytes · {item.MediaType.Value}\n" +
                       $"SHA-256 {item.Sha256.Value}\n" +
                       $"Window/display identity: {item.IdentityState}; scale: {item.ScaleState}" +
                       (item.UiScale is null ? string.Empty : $" ({item.UiScale.Value:0.##}×)") +
                       $"\nModel access: local; remote {(settingsState.VisualCaptureSettings?.Preferences.AllowRemoteModelAccess == true ? "enabled" : "disabled")}",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            preview.Children.Add(new TextBlock
            {
                Text = "No stored frame selected. The preview uses the exact bytes agents inspect.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        string availability = snapshot is null
            ? "Visual capture service unavailable."
            : snapshot.Availability.IsAvailable
                ? $"XDG portal v{snapshot.Availability.PortalVersion} available · targets: {string.Join(", ", snapshot.Availability.AvailableTargets)}"
                : $"Portal unavailable · {snapshot.Availability.Error}";
        return Page("Visual verification",
            "Capture one frame through the XDG Desktop Portal. Every request shows portal consent; Harness.NET cannot capture in the background or control input.",
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new Border { Classes = { "card" }, Child = new TextBlock { Text = availability + "\n" + (snapshot?.PrivateStorageDescription ?? string.Empty), TextWrapping = TextWrapping.Wrap } },
                    enabled,
                    new TextBlock { Text = "Maximum frame size (MiB)", FontWeight = FontWeight.SemiBold }, maximumMiB,
                    new TextBlock { Text = "Retention (days)", FontWeight = FontWeight.SemiBold }, retentionDays,
                    new TextBlock { Text = "Maximum frames per goal", FontWeight = FontWeight.SemiBold }, maximumPerGoal,
                    new Border { Classes = { "card", "attention" }, Child = new StackPanel { Spacing = 6, Children = { remote, new TextBlock { Text = "Off by default. Enabling this permits selected remote model providers to receive exact screenshot bytes.", TextWrapping = TextWrapping.Wrap } } } },
                    save,
                    new Separator(),
                    new TextBlock { Text = "Selected goal evidence", FontSize = 18, FontWeight = FontWeight.SemiBold },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { target, capture, refresh } },
                    captures,
                    delete,
                    new Border { Classes = { "card" }, Child = preview },
                    new TextBlock { Text = settingsState.Status ?? string.Empty, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                },
            });
    }

    private Control McpConnectionCard(McpConnectionSettingsView connection)
    {
        TextBox endpoint = ProviderTextBox(connection.Endpoint.Value,
            $"{connection.Name.Value} MCP endpoint");
        NumericUpDown timeout = ProviderNumber(
            connection.RequestTimeout.Value, 1, 3_600,
            $"{connection.Name.Value} MCP request timeout seconds");
        ComboBox kind = new()
        {
            ItemsSource = Enum.GetValues<McpConnectionKind>(),
            SelectedItem = connection.Kind,
            MinWidth = 220,
        };
        AutomationProperties.SetName(kind, $"{connection.Name.Value} MCP connection kind");
        TextBox clientId = ProviderTextBox(connection.ClientId?.Value ?? string.Empty,
            $"{connection.Name.Value} Harness control client ID");
        TextBox bearerToken = new()
        {
            PasswordChar = '●',
            PlaceholderText = connection.HasBearerToken
                ? "Stored in Secret Service; paste to replace"
                : "Paste worker bearer token",
        };
        AutomationProperties.SetName(bearerToken,
            $"{connection.Name.Value} Harness control bearer token");
        TextBox allowedTools = new()
        {
            Text = string.Join(Environment.NewLine,
                connection.AllowedTools.Select(tool => tool.Value)),
            AcceptsReturn = true,
            Height = 100,
        };
        AutomationProperties.SetName(allowedTools,
            $"{connection.Name.Value} Harness control allowed tool IDs");
        CheckBox enabled = new()
        {
            Content = "Enabled",
            IsChecked = connection.IsEnabled,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(enabled, $"Enable {connection.Name.Value} MCP connection");
        Button save = new() { Content = "Save", IsEnabled = !settingsState.IsBusy };
        save.Classes.Add("command");
        save.Click += async (_, _) => await store.SaveMcpConnectionAsync(new(
            connection.Name,
            new(endpoint.Text ?? string.Empty),
            new(decimal.ToInt32(timeout.Value ?? 0)),
            (McpConnectionKind)(kind.SelectedItem ?? McpConnectionKind.ReadOnly),
            kind.SelectedItem is McpConnectionKind.HarnessControl
                ? new(clientId.Text ?? string.Empty)
                : null,
            kind.SelectedItem is McpConnectionKind.HarnessControl &&
                !string.IsNullOrWhiteSpace(bearerToken.Text)
                ? new(bearerToken.Text)
                : null,
            kind.SelectedItem is McpConnectionKind.HarnessControl
                ? Lines(allowedTools.Text).Select(tool =>
                    new McpAllowedToolName(tool)).ToArray()
                : [],
            enabled.IsChecked == true), cancellationToken);
        Button remove = new() { Content = "Remove", IsEnabled = !settingsState.IsBusy };
        remove.Classes.Add("danger");
        AutomationProperties.SetName(remove, $"Remove {connection.Name.Value} MCP connection");
        remove.Click += async (_, _) => await store.DeleteMcpConnectionAsync(
            connection.Name, cancellationToken);

        Grid fields = new()
        {
            ColumnDefinitions = new("*,Auto"),
            RowDefinitions = new("Auto,Auto,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 10,
            Children = { endpoint },
        };
        Grid.SetColumn(timeout, 1);
        fields.Children.Add(timeout);
        Grid.SetRow(kind, 1);
        fields.Children.Add(kind);
        Grid.SetRow(clientId, 1);
        Grid.SetColumn(clientId, 1);
        fields.Children.Add(clientId);
        Grid.SetRow(bearerToken, 2);
        fields.Children.Add(bearerToken);
        Grid.SetRow(allowedTools, 2);
        Grid.SetColumn(allowedTools, 1);
        fields.Children.Add(allowedTools);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { enabled, save, remove },
        };
        string protocol = connection.NegotiatedProtocolVersion is null
            ? "No protocol negotiated"
            : $"MCP {connection.NegotiatedProtocolVersion}";
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
                        Text = connection.Name.Value,
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = $"{connection.State} · {connection.Kind} · {protocol} · {connection.DiscoveredTools} tool(s), {connection.AgentEligibleTools} eligible, {connection.RejectedTools} rejected" +
                            (connection.Kind is McpConnectionKind.HarnessControl
                                ? connection.HasBearerToken
                                    ? " · bearer token stored"
                                    : " · bearer token missing"
                                : string.Empty),
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    fields,
                    actions,
                    new TextBlock
                    {
                        Text = connection.Message ?? (connection.RequiresRestart
                            ? "Restart required before this saved configuration becomes active."
                            : "Connection discovery is ready."),
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
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

    private static TextBox Multiline(string value, string accessibleName, double height)
    {
        TextBox field = new()
        {
            Text = value,
            AcceptsReturn = true,
            Height = height,
            TextWrapping = TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(field, accessibleName);
        return field;
    }

    private static IReadOnlyList<string> Lines(string? value) =>
        (value ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

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

    private sealed record VisualCaptureChoice(VisualCaptureView Capture)
    {
        public override string ToString() =>
            $"{Capture.CreatedAt.LocalDateTime:g} · {Capture.Target} · {Capture.PixelSize.Width}×{Capture.PixelSize.Height}";
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
