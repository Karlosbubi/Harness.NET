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
    public async Task Visual_settings_expose_consent_privacy_limits_and_exact_frame_controls()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            visualCaptureService: new VisualCaptureService());
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
                category.Id is SettingsCategoryId.VisualVerification);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Enable visual verification capture");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Allow remote model access to visual captures");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Capture one visual verification frame");
            string text = string.Join('\n', window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(block => block.Text));
            Assert.Contains("XDG portal v3 available", text, StringComparison.Ordinal);
            Assert.Contains("Off by default", text, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class VisualCaptureService : IVisualCaptureService
    {
        private static readonly VisualCaptureSettingsSnapshot Settings = new(
            VisualCapturePreferences.Default,
            new(true, 3,
                [VisualCaptureTarget.UserSelection, VisualCaptureTarget.Window], null, null),
            "Private XDG state; excluded from repositories and backups.");

        public ValueTask<VisualCaptureSettingsSnapshot> GetSettingsAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Settings);

        public ValueTask<VisualCaptureSettingsResult> SaveSettingsAsync(
            VisualCapturePreferences preferences,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureSettingsResult(
                Settings with { Preferences = preferences }, null, null));

        public ValueTask<VisualCaptureResult> CaptureAsync(
            VisualCaptureRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureResult(
                VisualCaptureOutcome.Cancelled, null, "capture_cancelled", "Cancelled"));

        public ValueTask<VisualCaptureListResult> ListAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureListResult([], null, null));

        public ValueTask<VisualCaptureInspectionResult> InspectAsync(
            GoalId goalId,
            VisualCaptureId captureId,
            VisualCaptureModelAccess access,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureInspectionResult(
                VisualCaptureOutcome.NotFound, null, "capture_not_found", "Not found"));

        public ValueTask<bool> DeleteAsync(
            GoalId goalId,
            VisualCaptureId captureId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask CleanupAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

}
