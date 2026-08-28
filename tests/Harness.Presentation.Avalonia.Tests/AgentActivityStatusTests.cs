using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Avalonia.Tests;

public sealed class AgentActivityStatusTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-28T18:05:00Z");

    [Fact]
    public void Idle_workflow_has_no_status_affordance()
    {
        AgentActivityStatusView view = AgentActivityStatusProjector.Project(
            GoalManagementState.Initial, Now);

        Assert.False(view.IsVisible);
        Assert.Empty(view.CompactText);
    }

    [Fact]
    public void Active_model_call_reports_role_elapsed_time_and_observable_age()
    {
        DateTimeOffset started = Now.AddMinutes(-5);
        GoalWorkflowSnapshot workflow = Workflow(
            new(1, GoalWorkflowCheckpointKind.Started, WorkflowActor.System,
                new("Workflow started."), started),
            new(2, GoalWorkflowCheckpointKind.LeadCallStarted, WorkflowActor.Lead,
                new("Lead model call started."), Now.AddMinutes(-2)));
        GoalManagementState goals = GoalManagementState.Initial with
        {
            IsWorkflowRunning = true,
            WorkflowOperationName = "Generate plan",
            WorkflowOperationStartedAt = started,
            Workflow = workflow,
        };

        AgentActivityStatusView view = AgentActivityStatusProjector.Project(goals, Now);

        Assert.True(view.IsVisible);
        Assert.Equal("Lead · waiting for model · 5m 00s", view.CompactText);
        Assert.Contains("last observable update 2m 00s ago", view.Details,
            StringComparison.Ordinal);
        Assert.Contains("18:03:00 · Lead · Lead model call started.", view.Details,
            StringComparison.Ordinal);
        Assert.DoesNotContain('%', view.Details);
    }

    [Fact]
    public void Starting_call_without_checkpoint_is_truthful_and_timeline_is_bounded()
    {
        DateTimeOffset started = Now.AddSeconds(-12);
        GoalManagementState starting = GoalManagementState.Initial with
        {
            IsWorkflowRunning = true,
            WorkflowOperationName = "Continue workflow",
            WorkflowOperationStartedAt = started,
        };

        AgentActivityStatusView noCheckpoint =
            AgentActivityStatusProjector.Project(starting, Now);

        Assert.Equal("Starting workflow · 12s", noCheckpoint.CompactText);
        Assert.Contains("No durable workflow checkpoint has arrived yet.",
            noCheckpoint.Details, StringComparison.Ordinal);

        GoalWorkflowActivityView[] activities = Enumerable.Range(1, 10)
            .Select(sequence => new GoalWorkflowActivityView(
                sequence,
                GoalWorkflowCheckpointKind.ImplementationProduced,
                WorkflowActor.Implementer,
                new($"Checkpoint {sequence}"),
                started.AddSeconds(sequence)))
            .ToArray();
        AgentActivityStatusView bounded = AgentActivityStatusProjector.Project(
            starting with { Workflow = Workflow(activities) }, Now);

        Assert.DoesNotContain("Checkpoint 1\n", bounded.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkpoint 2\n", bounded.Details, StringComparison.Ordinal);
        Assert.Contains("Checkpoint 3", bounded.Details, StringComparison.Ordinal);
        Assert.Contains("Checkpoint 10", bounded.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_control_is_accessible_expandable_and_uses_existing_cancellation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            using AgentActivityStatusControl control = new();
            bool cancelled = false;
            control.CancelRequested += () => cancelled = true;
            control.Update(GoalManagementState.Initial with
            {
                IsWorkflowRunning = true,
                WorkflowOperationName = "Generate plan",
                WorkflowOperationStartedAt = TimeProvider.System.GetUtcNow(),
            });

            Button button = Assert.IsType<Button>(control.Control);
            Assert.True(button.IsVisible);
            Assert.StartsWith("Agent activity:", AutomationProperties.GetName(button),
                StringComparison.Ordinal);
            Flyout flyout = Assert.IsType<Flyout>(button.Flyout);
            StackPanel content = Assert.IsType<StackPanel>(flyout.Content);
            Button cancel = Assert.IsType<Button>(content.Children[^1]);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(cancelled);
        }, CancellationToken.None);
    }

    private static GoalWorkflowSnapshot Workflow(
        params GoalWorkflowActivityView[] activities) => new(
        new("run-status"),
        new("goal-status"),
        GoalWorkflowState.Running,
        new(0),
        [],
        activities,
        [],
        CanResume: false,
        RequiresUserDirection: false);
}
