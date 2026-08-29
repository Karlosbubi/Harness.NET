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
    public async Task Mcp_settings_manage_stateless_connections_from_the_first_slice()
    {
        McpSettingsService mcp = new();
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            mcpSettingsService: mcp);
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
                category.Id is SettingsCategoryId.McpConnections);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Add connection"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Refresh active connections"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "New MCP connection kind");
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "New Harness control bearer token");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "New Harness control client ID");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "New Harness control allowed tool IDs");
            ComboBox kind = Assert.Single(window.GetLogicalDescendants().OfType<ComboBox>(),
                control => AutomationProperties.GetName(control) == "New MCP connection kind");
            kind.SelectedItem = McpConnectionKind.HarnessControl;
            Dispatcher.UIThread.RunJobs();
            TextBox allowedTools = Assert.Single(
                window.GetLogicalDescendants().OfType<TextBox>(),
                control => AutomationProperties.GetName(control) ==
                    "New Harness control allowed tool IDs");
            Assert.Contains("harness_create_goal", allowedTools.Text,
                StringComparison.Ordinal);
            Assert.Contains("harness_decide_commit", allowedTools.Text,
                StringComparison.Ordinal);
            string text = string.Join('\n', window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(block => block.Text));
            Assert.Contains("2026-07-28", text, StringComparison.Ordinal);
            Assert.Contains("1 eligible", text, StringComparison.Ordinal);
            Assert.Contains("fail closed", text, StringComparison.OrdinalIgnoreCase);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class McpSettingsService : IMcpSettingsService
    {
        private readonly McpSettingsSnapshot snapshot = new([
            new(
                new("docs"),
                new("https://docs.example.test/mcp"),
                new(30),
                McpConnectionKind.ReadOnly,
                ClientId: null,
                AllowedTools: [],
                IsEnabled: true,
                State: McpConnectionState.Ready,
                NegotiatedProtocolVersion: "2026-07-28",
                DiscoveredTools: 2,
                AgentEligibleTools: 1,
                RejectedTools: 1,
                Message: null,
                RequiresRestart: false),
        ]);

        public ValueTask<McpSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public ValueTask<McpSettingsSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public ValueTask<McpSettingsResult> SaveAsync(
            McpConnectionSettingsUpdate request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new McpSettingsResult(snapshot, null, null));

        public ValueTask<McpSettingsResult> DeleteAsync(
            McpConnectionName name,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new McpSettingsResult(snapshot, null, null));
    }

}
