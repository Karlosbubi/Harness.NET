using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Evidence;
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
    public void Timeline_excludes_checkpoints_from_an_earlier_operation()
    {
        DateTimeOffset started = Now.AddMinutes(-1);
        GoalManagementState goals = GoalManagementState.Initial with
        {
            IsWorkflowRunning = true,
            WorkflowOperationStartedAt = started,
            Workflow = Workflow(
                new(1, GoalWorkflowCheckpointKind.Accepted, WorkflowActor.System,
                    new("Earlier operation accepted."), started.AddSeconds(-1)),
                new(2, GoalWorkflowCheckpointKind.Started, WorkflowActor.System,
                    new("Current operation started."), started)),
        };

        AgentActivityStatusView view = AgentActivityStatusProjector.Project(goals, Now);

        Assert.DoesNotContain("Earlier operation", view.Details, StringComparison.Ordinal);
        Assert.Contains("Current operation started.", view.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_typed_operation_overrides_waiting_phase_without_exposing_payloads()
    {
        DateTimeOffset started = Now.AddMinutes(-1);
        GoalManagementState goals = GoalManagementState.Initial with
        {
            SelectedGoalId = new("goal-status"),
            IsWorkflowRunning = true,
            WorkflowOperationStartedAt = started,
            Workflow = Workflow(new GoalWorkflowActivityView(
                1, GoalWorkflowCheckpointKind.ImplementerCallStarted,
                WorkflowActor.Implementer, new("Implementer call started."), started)),
        };
        ToolEvidenceSnapshot evidence = new(
            [new(new("tool-1"), "goal-status", new("correlation-1"), ToolKind.Build,
                "sensitive request", ToolEvidenceState.Running, "sensitive result",
                Now.AddSeconds(-5), CompletedAt: null)],
            null,
            null);

        AgentActivityStatusView view = AgentActivityStatusProjector.Project(goals, Now, evidence);

        Assert.Equal("Build · running · 1m 00s", view.CompactText);
        Assert.Contains("Build · Running", view.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", view.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_stream_and_read_only_tool_activity_are_truthful_and_payload_free()
    {
        DateTimeOffset started = Now.AddMinutes(-1);
        GoalManagementState goals = GoalManagementState.Initial with
        {
            SelectedGoalId = new("goal-status"),
            IsWorkflowRunning = true,
            WorkflowOperationStartedAt = started,
        };
        AgentActivitySnapshot provider = new([
            new(new("activity-1"), new("goal-status"), AgentRole.Implementer,
                AgentActivityKind.ProviderRequest, new("model_response"),
                AgentActivityPhase.ReceivingResponse, Now.AddSeconds(-8), Now.AddSeconds(-2)),
        ]);

        AgentActivityStatusView receiving = AgentActivityStatusProjector.Project(
            goals, Now, sessionActivity: provider);

        Assert.Equal("Implementer · receiving response · 1m 00s", receiving.CompactText);
        Assert.Contains("last observable update just now", receiving.Details,
            StringComparison.Ordinal);
        Assert.DoesNotContain("model_response", receiving.Details, StringComparison.Ordinal);

        AgentActivitySnapshot coalesced = new([
            .. provider.Items,
            new(new("activity-2"), new("goal-status"), AgentRole.Implementer,
                AgentActivityKind.ToolInvocation, new("read_file_range"),
                AgentActivityPhase.Running, Now.AddSeconds(-1), Now.AddSeconds(-1)),
        ]);
        AgentActivityStatusView multiple = AgentActivityStatusProjector.Project(
            goals, Now, sessionActivity: coalesced);

        Assert.Equal("2 agent operations · active · 1m 00s", multiple.CompactText);
        Assert.Contains("Read file range · running", multiple.Details,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Review_correction_is_identified_as_an_implementer_retry()
    {
        DateTimeOffset started = Now.AddMinutes(-1);
        GoalManagementState goals = GoalManagementState.Initial with
        {
            SelectedGoalId = new("goal-status"),
            IsWorkflowRunning = true,
            WorkflowOperationStartedAt = started,
            Workflow = Workflow(
                new(1, GoalWorkflowCheckpointKind.ReviewCompleted, WorkflowActor.Reviewer,
                    new("Reviewer requested a correction."), started.AddSeconds(1)),
                new(2, GoalWorkflowCheckpointKind.ImplementerCallStarted,
                    WorkflowActor.Implementer, new("Correction call started."),
                    started.AddSeconds(2))),
        };
        AgentActivitySnapshot activity = new([
            new(new("activity-retry"), new("goal-status"), AgentRole.Implementer,
                AgentActivityKind.ProviderRequest, new("model_response"),
                AgentActivityPhase.WaitingForResponse, started.AddSeconds(2),
                started.AddSeconds(2)),
        ]);

        AgentActivityStatusView view = AgentActivityStatusProjector.Project(
            goals, Now, sessionActivity: activity);

        Assert.StartsWith("Implementer retry · contacting model", view.CompactText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_control_is_accessible_expandable_and_uses_existing_cancellation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            using AgentActivityStatusControl control = new(
                new ToolEvidenceService(), new AgentActivityReader());
            bool cancelled = false;
            bool goalRequested = false;
            bool evidenceRequested = false;
            control.CancelRequested += () => cancelled = true;
            control.GoalRequested += () => goalRequested = true;
            control.EvidenceRequested += () => evidenceRequested = true;
            control.Update(GoalManagementState.Initial with
            {
                SelectedGoalId = new("goal-status"),
                IsWorkflowRunning = true,
                WorkflowOperationName = "Generate plan",
                WorkflowOperationStartedAt = TimeProvider.System.GetUtcNow(),
                Workflow = Workflow() with
                {
                    Evidence = [new(1, new("Build"), new("Build passed."))],
                },
            });

            Button button = Assert.IsType<Button>(control.Control);
            Assert.True(button.IsVisible);
            Assert.StartsWith("Agent activity:", AutomationProperties.GetName(button),
                StringComparison.Ordinal);
            Flyout flyout = Assert.IsType<Flyout>(button.Flyout);
            StackPanel content = Assert.IsType<StackPanel>(flyout.Content);
            StackPanel actions = Assert.IsType<StackPanel>(content.Children[^1]);
            Button openGoal = Assert.IsType<Button>(actions.Children[0]);
            Button openEvidence = Assert.IsType<Button>(actions.Children[1]);
            Button cancel = Assert.IsType<Button>(actions.Children[2]);
            Assert.Equal("Open active goal conversation", AutomationProperties.GetName(openGoal));
            Assert.Equal("Open active goal workflow evidence",
                AutomationProperties.GetName(openEvidence));
            Assert.True(openEvidence.IsEnabled);
            openGoal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            openEvidence.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(goalRequested);
            Assert.True(evidenceRequested);
            Assert.True(cancelled);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Status_control_loads_typed_evidence_immediately()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            ToolEvidenceService evidence = new();
            using AgentActivityStatusControl control = new(evidence, new AgentActivityReader());

            control.Update(GoalManagementState.Initial with
            {
                SelectedGoalId = new("goal-status"),
                IsWorkflowRunning = true,
                WorkflowOperationStartedAt = TimeProvider.System.GetUtcNow(),
            });

            Assert.Equal(1, evidence.CallCount);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Status_renders_at_compact_width_without_stealing_focus_and_hides_on_recovery()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            TextBox composer = new() { Text = "Keep focus here" };
            using AgentActivityStatusControl control = new(
                new ToolEvidenceService(), new AgentActivityReader());
            Window window = new()
            {
                Width = 320,
                Height = 180,
                Content = new StackPanel { Children = { composer, control.Control } },
            };
            window.Show();
            composer.Focus();
            control.Update(GoalManagementState.Initial with
            {
                SelectedGoalId = new("goal-status"),
                IsWorkflowRunning = true,
                WorkflowOperationName = "Generate plan",
                WorkflowOperationStartedAt = TimeProvider.System.GetUtcNow(),
            });
            Dispatcher.UIThread.RunJobs();

            Button button = Assert.IsType<Button>(control.Control);
            using Bitmap rendered = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
            Assert.True(rendered.PixelSize.Width > 0);
            Assert.True(button.Bounds.Width <= window.ClientSize.Width);
            Assert.True(composer.IsFocused);

            control.Update(GoalManagementState.Initial);
            Dispatcher.UIThread.RunJobs();
            Assert.False(button.IsVisible);
            window.Close();
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

    private sealed class ToolEvidenceService : IToolEvidenceService
    {
        internal int CallCount { get; private set; }

        public ValueTask<ToolEvidenceSnapshot> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(new ToolEvidenceSnapshot([], null, null));
        }
    }

    private sealed class AgentActivityReader : IAgentActivityReader
    {
        public event Action? Changed;

        public AgentActivitySnapshot GetSnapshot() => new([]);

        internal void Publish() => Changed?.Invoke();
    }
}
