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

}
