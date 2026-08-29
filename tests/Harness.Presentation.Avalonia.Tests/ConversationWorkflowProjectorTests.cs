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
    public void Workflow_cards_project_durable_and_degraded_states_without_commands()
    {
        AvaloniaShellState shell = ApprovedGoalShell();
        GoalView goal = shell.Goals.SelectedGoal!;
        PlanView plan = new(
            new("plan-1"),
            goal.Id,
            new(2),
            "1. Make the bounded change\n2. Verify it",
            PlanState.Denied,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        IReadOnlyList<ConversationWorkflowCard> cards = ConversationWorkflowProjector.Project(
            shell.Goals with { CurrentPlan = plan },
            "Provider unavailable");

        Assert.Equal(ConversationWorkflowCardState.Approved, cards[0].State);
        Assert.Contains(cards, item =>
            item.Kind is ConversationWorkflowCardKind.Plan &&
            item.State is ConversationWorkflowCardState.Denied);
        Assert.Contains(cards, item => item.State is ConversationWorkflowCardState.Failed);
        Assert.Equal(
            [
                ConversationWorkflowCardState.Loading,
                ConversationWorkflowCardState.Unavailable,
                ConversationWorkflowCardState.Stale,
                ConversationWorkflowCardState.Pending,
                ConversationWorkflowCardState.Active,
                ConversationWorkflowCardState.Paused,
                ConversationWorkflowCardState.Approved,
                ConversationWorkflowCardState.Denied,
                ConversationWorkflowCardState.Failed,
                ConversationWorkflowCardState.Cancelled,
                ConversationWorkflowCardState.Recovered,
                ConversationWorkflowCardState.Completed,
            ],
            Enum.GetValues<ConversationWorkflowCardState>());
    }

    [Fact]
    public void Workflow_actions_expose_only_the_current_plan_decision()
    {
        AvaloniaShellState shell = ApprovedGoalShell();
        GoalView draft = shell.Goals.SelectedGoal! with { State = GoalState.Draft };
        GoalManagementState draftState = shell.Goals with
        {
            Items = [draft],
            CurrentPlan = null,
        };
        ConversationWorkflowCard missingPlan = Assert.Single(
            ConversationWorkflowProjector.Project(draftState),
            card => card.Kind is ConversationWorkflowCardKind.Plan);

        Assert.Equal(
            [
                ConversationWorkflowActionKind.StartPlanning,
                ConversationWorkflowActionKind.WritePlan,
            ],
            ConversationWorkflowActionProjector.Project(missingPlan, draftState)
                .Select(action => action.Kind));

        PlanView pendingPlan = new(
            new("plan-1"),
            draft.Id,
            new(1),
            "1. Implement\n2. Verify",
            PlanState.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        GoalManagementState pendingState = draftState with { CurrentPlan = pendingPlan };
        ConversationWorkflowCard planCard = Assert.Single(
            ConversationWorkflowProjector.Project(pendingState),
            card => card.Kind is ConversationWorkflowCardKind.Plan);

        Assert.Equal(
            [
                ConversationWorkflowActionKind.ApprovePlan,
                ConversationWorkflowActionKind.RequestPlanChanges,
            ],
            ConversationWorkflowActionProjector.Project(planCard, pendingState)
                .Select(action => action.Kind));
    }

    [Fact]
    public void Failed_role_card_exposes_exact_retry_and_abort_recovery()
    {
        AvaloniaShellState shell = ApprovedGoalShell();
        GoalView goal = shell.Goals.SelectedGoal!;
        GoalWorkflowSnapshot workflow = new(
            new("run-retry"),
            goal.Id,
            GoalWorkflowState.NeedsDirection,
            new(0),
            [],
            [new(1, GoalWorkflowCheckpointKind.UserDirectionRequired,
                WorkflowActor.System, new("Provider unavailable; inspect cost evidence."),
                DateTimeOffset.UtcNow)],
            [new(1, new("Recovery notice"), new("Provider unavailable; inspect cost evidence."))],
            CanResume: false,
            RequiresUserDirection: true,
            RetryRole: GoalWorkflowRetryRole.Reviewer);
        GoalManagementState state = shell.Goals with
        {
            Workflow = workflow,
            ModelSelections = [],
        };
        ConversationWorkflowCard runCard = Assert.Single(
            ConversationWorkflowProjector.Project(state),
            card => card.Id == "run.run-retry");
        ConversationWorkflowAction[] actions =
            ConversationWorkflowActionProjector.Project(runCard, state).ToArray();

        Assert.Equal(
            [ConversationWorkflowActionKind.RetryRun,
                ConversationWorkflowActionKind.AbortGoal],
            actions.Select(action => action.Kind));
        Assert.Equal("Retry Reviewer", actions[0].Label);
        Assert.Equal("Current run · Needs your direction", runCard.Title);
        Assert.Equal(ConversationWorkflowCardState.Paused, runCard.State);
        Assert.Contains("Now:", runCard.Summary, StringComparison.Ordinal);
        Assert.Contains("Result so far:", runCard.Summary, StringComparison.Ordinal);
        Assert.Contains("Next: Retry Reviewer as-is", runCard.Summary, StringComparison.Ordinal);
        Assert.Contains("explicitly retrying Reviewer", runCard.Details,
            StringComparison.Ordinal);
        ConversationWorkflowCard direction = Assert.Single(
            ConversationWorkflowProjector.Project(state),
            card => card.Title == "User direction required");
        Assert.Equal(ConversationWorkflowCardState.Paused, direction.State);
        Assert.Contains("Reviewer did not produce a usable decision", direction.Summary,
            StringComparison.Ordinal);
        Assert.Contains("Technical detail: Provider unavailable", direction.Details,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ConversationWorkflowProjector.Project(state),
            card => card.Title == "Recovery notice");

        GoalManagementState remoteState = state with
        {
            ModelSelections =
            [
                new(goal.Id, AgentRole.Reviewer, new("remote"), new("review-model"),
                    ModelAccess.Remote, IsExplicit: true, DateTimeOffset.UtcNow),
            ],
        };
        ConversationWorkflowCard remoteRunCard = Assert.Single(
            ConversationWorkflowProjector.Project(remoteState),
            card => card.Id == "run.run-retry");
        Assert.Equal(
            [ConversationWorkflowActionKind.RetryRun,
                ConversationWorkflowActionKind.AbortGoal],
            ConversationWorkflowActionProjector.Project(remoteRunCard, remoteState)
                .Select(item => item.Kind));

        GoalView cappedGoal = goal with
        {
            RemoteBudget = new(5_000_000),
        };
        GoalManagementState cappedState = remoteState with
        {
            Items = [cappedGoal],
        };
        ConversationWorkflowCard cappedRunCard = Assert.Single(
            ConversationWorkflowProjector.Project(cappedState),
            card => card.Id == "run.run-retry");
        Assert.Equal(
            [ConversationWorkflowActionKind.RetryRun,
                ConversationWorkflowActionKind.ExtendBudget,
                ConversationWorkflowActionKind.AbortGoal],
            ConversationWorkflowActionProjector.Project(cappedRunCard, cappedState)
                .Select(item => item.Kind));

        GoalManagementState correctionState = state with
        {
            Workflow = workflow with
            {
                State = GoalWorkflowState.Running,
                CanResume = true,
                RequiresUserDirection = false,
                RetryRole = null,
            },
            IsWorkflowRunning = false,
        };
        ConversationWorkflowCard correctionCard = Assert.Single(
            ConversationWorkflowProjector.Project(correctionState),
            card => card.Id == "run.run-retry");
        Assert.Equal(
            ConversationWorkflowActionKind.ContinueRun,
            Assert.Single(ConversationWorkflowActionProjector.Project(
                correctionCard, correctionState)).Kind);
    }

}
