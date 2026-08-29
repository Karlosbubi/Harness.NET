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
    public async Task Keybinding_settings_expose_conflicts_reset_and_safe_import_export()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            keybindingSettingsService: new KeybindingSettingsService());
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
                category.Id is SettingsCategoryId.Keybindings);
            Dispatcher.UIThread.RunJobs();

            TextBox chat = window.GetLogicalDescendants().OfType<TextBox>().Single(control =>
                AutomationProperties.GetName(control) == "Shortcut for Show Chat");
            TextBox quickOpen = window.GetLogicalDescendants().OfType<TextBox>().Single(control =>
                AutomationProperties.GetName(control) == "Shortcut for Go to file");
            chat.Text = quickOpen.Text;
            Dispatcher.UIThread.RunJobs();

            Button save = window.GetLogicalDescendants().OfType<Button>().Single(control =>
                AutomationProperties.GetName(control) == "Save validated keybindings");
            string text = string.Join('\n', window.GetLogicalDescendants().OfType<TextBlock>()
                .Select(block => block.Text));
            Assert.False(save.IsEnabled);
            Assert.Contains("conflicts", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Reset all keybindings to defaults");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Export keybindings as safe JSON");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Validate and import keybinding JSON");
            ComboBox inputMode = Assert.Single(window.GetLogicalDescendants().OfType<ComboBox>(),
                control => AutomationProperties.GetName(control) == "Editor keyboard input mode");
            Assert.Equal(EditorInputMode.Standard, inputMode.SelectedItem);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class KeybindingSettingsService : IKeybindingSettingsService
    {
        private KeybindingSettingsSnapshot snapshot = KeybindingSettingsSnapshot.Default;

        public ValueTask<KeybindingSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public KeybindingValidationResult Validate(KeybindingUpdateRequest request)
        {
            string[] duplicates = request.Entries.SelectMany(entry =>
                    entry.GestureText.Split(';', StringSplitOptions.TrimEntries |
                                               StringSplitOptions.RemoveEmptyEntries))
                .GroupBy(text => text, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            IReadOnlyList<KeybindingIssue> issues = duplicates.Select(text => new KeybindingIssue(
                KeybindingIssueKind.Conflict, null, $"{text} conflicts with another command.")).ToArray();
            return new(issues.Count == 0, issues, snapshot.Bindings);
        }

        public ValueTask<KeybindingSettingsSnapshot> SaveAsync(
            KeybindingUpdateRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public ValueTask<KeybindingSettingsSnapshot> ResetAsync(
            CancellationToken cancellationToken = default)
        {
            snapshot = KeybindingSettingsSnapshot.Default;
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<string> ExportAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("{\"format\":\"harness-keybindings-v1\",\"bindings\":[]}");

        public ValueTask<KeybindingSettingsSnapshot> ImportAsync(
            string document,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }

}
