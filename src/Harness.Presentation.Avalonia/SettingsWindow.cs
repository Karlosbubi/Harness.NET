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
internal sealed partial class SettingsWindow : Window
{
    private static readonly string[] DefaultHarnessControlTools =
    [
        "harness_application", "harness_workspace", "harness_tree",
        "harness_read_range", "harness_git", "harness_git_history",
        "harness_git_commit", "harness_git_blame", "harness_project_graph",
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

    private static Control Labeled(string label, Control control, string? help = null)
    {
        StackPanel field = new() { Spacing = 4 };
        field.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
        field.Children.Add(control);
        if (help is not null) field.Children.Add(new TextBlock
        { Text = help, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        return field;
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
