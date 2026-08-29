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
    public void Settings_search_matches_stable_categories_and_related_terms()
    {
        Assert.Equal(14, SettingsCatalog.All.Count);
        Assert.Equal(
            SettingsCategoryId.InboundMcp,
            Assert.Single(SettingsCatalog.Filter("dogfood")).Id);
        Assert.Equal(
            SettingsCategoryId.Appearance,
            Assert.Single(SettingsCatalog.Filter("contrast")).Id);
        Assert.Equal(
            SettingsCategoryId.ModelsAndRoles,
            Assert.Single(SettingsCatalog.Filter("reviewer")).Id);
        Assert.Equal(
            SettingsCategoryId.ModelProviders,
            Assert.Single(SettingsCatalog.Filter("openrouter")).Id);
        Assert.Equal(
            SettingsCategoryId.McpConnections,
            Assert.Single(SettingsCatalog.Filter("stateless")).Id);
        Assert.Equal(
            SettingsCategoryId.AgentTools,
            Assert.Single(SettingsCatalog.Filter("definition")).Id);
        Assert.Equal(
            SettingsCategoryId.VisualVerification,
            Assert.Single(SettingsCatalog.Filter("screenshot")).Id);
        Assert.Equal(
            SettingsCategoryId.DocumentationAndDependencies,
            Assert.Single(SettingsCatalog.Filter("cyclonedx")).Id);
        Assert.Equal(
            SettingsCategoryId.StorageAndRecovery,
            Assert.Single(SettingsCatalog.Filter("backup")).Id);
        Assert.Equal(
            SettingsCategoryId.Editor,
            Assert.Single(SettingsCatalog.Filter("inlay")).Id);
        Assert.Equal(
            SettingsCategoryId.Keybindings,
            Assert.Single(SettingsCatalog.Filter("shortcut")).Id);
        Assert.Empty(SettingsCatalog.Filter("not-a-real-setting"));
        Assert.Equal(11, SettingsCatalog.All.Count(category => category.IsAvailable));
    }

}
