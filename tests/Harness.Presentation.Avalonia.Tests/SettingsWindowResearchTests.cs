using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Research_settings_expose_sources_offline_cache_dependency_and_explicit_export_controls()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            researchSettingsService: new ResearchSettingsService());
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.DocumentationAndDependencies);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Documentation index roots, one absolute path per line");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "MCP documentation tools, one connection/tool per line");
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Look up documentation"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Inspect dependency graph"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Preview package + SBOM diff"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Export current SBOM…"));
            string text = string.Join('\n', window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(block => block.Text));
            Assert.Contains("exact local/package docs → local index → configured MCP → web", text,
                StringComparison.Ordinal);
            Assert.Contains("Cache: 3 entries", text, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class ResearchSettingsService : IResearchSettingsService
    {
        private static readonly ResearchSettingsSnapshot Snapshot = new(
            true, true, true, true, false,
            ["/docs"], ["docs/search"], ["https://learn.microsoft.com/api/search"],
            ["https://api.nuget.org/v3/index.json"], ResearchRefreshMode.OnDemand,
            5, 12_000, 168, 30, 3, 1_024, null);

        public ValueTask<ResearchSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);

        public ValueTask<ResearchSettingsResult> SaveAsync(ResearchSettingsUpdate update,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResearchSettingsResult(Snapshot, null, null));

        public ValueTask<ResearchSettingsSnapshot> CleanupCacheAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
    }

}
