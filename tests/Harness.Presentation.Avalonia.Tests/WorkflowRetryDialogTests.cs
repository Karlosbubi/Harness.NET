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
    public async Task Workflow_retry_allows_model_only_retry_without_token_ceiling()
    {
        GoalModelCandidate reviewer = Candidate(
            "local", "reviewer", ModelAccess.Local, [AgentRole.Reviewer]);
        GoalModelCandidate leadOnly = Candidate(
            "local", "lead", ModelAccess.Local, [AgentRole.Lead]);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkflowRetryDialog dialog = new(
                GoalWorkflowRetryRole.Reviewer,
                [leadOnly, reviewer],
                reviewer,
                "The prior call was not replayed.");
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            AutoCompleteBox models = Assert.Single(
                dialog.GetLogicalDescendants().OfType<AutoCompleteBox>());
            Assert.Single(models.ItemsSource!.Cast<object>());
            TextBox guidance = Assert.Single(
                dialog.GetLogicalDescendants().OfType<TextBox>(),
                field => AutomationProperties.GetName(field) == "Guidance for Reviewer retry");
            guidance.Text = string.Empty;
            Button retry = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, "Retry Reviewer"));
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(reviewer, dialog.Result?.Model);
            Assert.Null(dialog.Result?.Guidance);
        }, CancellationToken.None);
    }

}
