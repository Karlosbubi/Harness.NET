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
    public async Task Inbound_mcp_settings_expose_accessible_names_for_policy_and_limit_fields()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore();
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
                category.Id is SettingsCategoryId.InboundMcp);
            Dispatcher.UIThread.RunJobs();

            string[] expectedNames =
            [
                "Allowed inbound MCP client IDs",
                "Allowed inbound MCP tool IDs",
                "Inbound MCP tool IDs requiring explicit approval",
                "Inbound MCP request timeout in seconds",
                "Inbound MCP result limit",
                "Inbound MCP audit retention",
            ];
            string[] actualNames = window.GetLogicalDescendants()
                .OfType<Control>()
                .Select(AutomationProperties.GetName)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();

            Assert.All(expectedNames, name => Assert.Contains(name, actualNames));
            window.Close();
        }, CancellationToken.None);
    }

}
