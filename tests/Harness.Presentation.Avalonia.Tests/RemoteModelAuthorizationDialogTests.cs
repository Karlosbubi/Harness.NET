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
    public async Task Remote_model_remains_visible_but_cannot_be_authorized_for_local_only_goal()
    {
        GoalView localOnly = ApprovedGoalShell().Goals.SelectedGoal! with
        {
            RemoteBudget = null,
        };
        GoalModelCandidate remote = Candidate(
            "OpenRouter", "remote-lead", ModelAccess.Remote, [AgentRole.Lead]);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            RemoteModelAuthorizationDialog dialog = new(localOnly, remote, AgentRole.Lead);
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            Button authorize = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, "Enable remote spend first"));
            Assert.False(authorize.IsEnabled);
            Assert.Contains("currently local-only", string.Join('\n', dialog
                .GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unlimited_goal_can_authorize_a_remote_model_without_adding_a_cap()
    {
        GoalView unlimited = ApprovedGoalShell().Goals.SelectedGoal! with
        {
            RemoteBudget = RemoteSpendPreference.Default.ToGoalBudget(),
        };
        GoalModelCandidate remote = Candidate(
            "OpenRouter", "remote-lead", ModelAccess.Remote, [AgentRole.Lead]);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            RemoteModelAuthorizationDialog dialog = new(unlimited, remote, AgentRole.Lead);
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            Button authorize = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, "Use remote model"));
            Assert.True(authorize.IsEnabled);
            Assert.Contains("Unlimited", string.Join('\n', dialog
                .GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            dialog.Close();
        }, CancellationToken.None);
    }

}
