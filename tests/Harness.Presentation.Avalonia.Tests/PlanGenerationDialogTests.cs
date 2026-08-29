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
    public async Task Plan_generation_shows_only_lead_compatible_models_and_prefers_configured_lead()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            GoalModelCandidate configuredLead = Candidate(
                "OpenRouter", "lead", ModelAccess.Remote, [AgentRole.Lead]);
            PlanGenerationDialog dialog = new(
                [
                    Candidate("Ollama", "plain", ModelAccess.Local, []),
                    Candidate("Ollama", "review", ModelAccess.Local, [AgentRole.Reviewer]),
                    configuredLead,
                ],
                configuredLead,
                "Disclosure");
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            AutoCompleteBox models = Assert.Single(
                dialog.GetLogicalDescendants().OfType<AutoCompleteBox>());
            Assert.True(models.IsVisible);
            Assert.True(models.Bounds.Height > 0);
            object model = Assert.Single(models.ItemsSource!.Cast<object>());
            Assert.Contains("OpenRouter/lead", models.SelectedItem?.ToString(),
                StringComparison.Ordinal);
            Assert.Contains("OpenRouter/lead", models.Text, StringComparison.Ordinal);
            Assert.Equal("Search provider or model", models.PlaceholderText);
            Assert.True(models.ItemFilter!("openrouter", model));
            Assert.False(models.ItemFilter!("ollama", model));
            Button showAll = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>(),
                button => AutomationProperties.GetName(button) == "Show all models");
            showAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(models.IsDropDownOpen);
            Assert.Null(models.SelectedItem);
            Assert.Equal(string.Empty, models.Text);
            Assert.Empty(dialog.GetLogicalDescendants().OfType<TextBox>());
            dialog.Close();
        }, CancellationToken.None);
    }

    private static GoalModelCandidate Candidate(
        string provider,
        string model,
        ModelAccess access,
        IReadOnlyList<AgentRole> supportedRoles) => new(
        new(provider),
        new(model),
        access,
        [new("tools")],
        supportedRoles,
        null,
        null,
        null,
        null);

}
