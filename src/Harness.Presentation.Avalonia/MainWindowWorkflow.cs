using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed partial class MainWindow : Window
{
    private Control CreateWorkflowCard(ConversationWorkflowCard item)
    {
        Border stateBadge = new()
        {
            Classes = { "workflow-state" },
            Child = new TextBlock { Text = item.State.ToString().ToUpperInvariant() },
        };
        stateBadge.Classes.Add(item.State switch
        {
            ConversationWorkflowCardState.Approved or ConversationWorkflowCardState.Completed or
                ConversationWorkflowCardState.Recovered => "success",
            ConversationWorkflowCardState.Paused => "attention",
            ConversationWorkflowCardState.Denied or ConversationWorkflowCardState.Failed or
                ConversationWorkflowCardState.Cancelled or ConversationWorkflowCardState.Stale => "attention",
            _ => "neutral",
        });
        Grid heading = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        heading.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(stateBadge, 1);
        heading.Children.Add(stateBadge);
        StackPanel content = new()
        {
            Spacing = 6,
            Children =
            {
                heading,
                new TextBlock { Text = item.Summary, TextWrapping = TextWrapping.Wrap },
            },
        };
        if (item.Details is { Length: > 0 } details && details != item.Summary)
        {
            content.Children.Add(new TextBlock
            {
                Text = details,
                Classes = { "muted" },
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 86,
            });
        }

        IReadOnlyList<ConversationWorkflowAction> actions =
            ConversationWorkflowActionProjector.Project(item, store.Current.Goals);
        if (actions.Count > 0)
        {
            StackPanel actionRow = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0),
            };
            foreach (ConversationWorkflowAction action in actions)
            {
                Button button = new() { Content = action.Label };
                button.Classes.Add(action.IsPrimary ? "primary" : "command");
                button.IsEnabled = !store.Current.Goals.IsBusy &&
                                   !store.Current.Goals.IsWorkflowRunning;
                AutomationProperties.SetName(button, action.Label);
                button.Click += async (_, _) => await ExecuteWorkflowActionAsync(action.Kind, item);
                actionRow.Children.Add(button);
            }
            content.Children.Add(actionRow);
        }

        Border card = new()
        {
            Classes = { "workflow-card" },
            Child = content,
            Margin = new Thickness(4, 0, 28, 9),
        };
        AutomationProperties.SetName(
            card,
            $"{item.Kind}: {item.Title}, {item.State}");
        AutomationProperties.SetAccessibilityView(card, AccessibilityView.Content);
        return card;
    }

    private async Task ExecuteWorkflowActionAsync(
        ConversationWorkflowActionKind action,
        ConversationWorkflowCard card)
    {
        GoalView? goal = store.Current.Goals.SelectedGoal;
        if (goal is null)
        {
            return;
        }

        switch (action)
        {
            case ConversationWorkflowActionKind.ConfigureGoal:
                await new GoalSettingsDialog(store, goal, cancellationToken).ShowDialog(this);
                break;
            case ConversationWorkflowActionKind.StartPlanning:
                await StartPlanningAsync(goal);
                break;
            case ConversationWorkflowActionKind.WritePlan:
                await WritePlanAsync(goal);
                break;
            case ConversationWorkflowActionKind.ApprovePlan:
                await ApprovePlanAsync(goal);
                break;
            case ConversationWorkflowActionKind.RequestPlanChanges:
                await RequestPlanChangesAsync(goal);
                break;
            case ConversationWorkflowActionKind.ContinueRun:
                await ContinueRunAsync(goal);
                break;
            case ConversationWorkflowActionKind.RetryRun:
                await RetryRunAsync(goal);
                break;
            case ConversationWorkflowActionKind.AbortGoal:
                await AbortGoalAsync(goal);
                break;
            case ConversationWorkflowActionKind.ExtendBudget:
                await new BudgetExtensionDialog(store, goal, cancellationToken).ShowDialog(this);
                break;
            case ConversationWorkflowActionKind.CancelRun:
                store.CancelGoalWorkflow();
                break;
            case ConversationWorkflowActionKind.ReviewAcceptedChanges:
                await store.RefreshCommitAsync(goal.Id, cancellationToken);
                break;
            case ConversationWorkflowActionKind.ApproveRestore:
                await ApproveRestoreAsync(goal, card);
                break;
            case ConversationWorkflowActionKind.DenyRestore:
                await DenyRestoreAsync(goal, card);
                break;
            case ConversationWorkflowActionKind.ReviewCommitPreview:
                await new CommitApprovalDialog(store, cancellationToken).ShowDialog(this);
                break;
            case ConversationWorkflowActionKind.ApproveCommit:
                await DecideCommitAsync(resuming: false);
                break;
            case ConversationWorkflowActionKind.DenyCommit:
                await DenyCommitAsync();
                break;
            case ConversationWorkflowActionKind.ResumeCommit:
                await DecideCommitAsync(resuming: true);
                break;
            case ConversationWorkflowActionKind.ReviewBranchHandoff:
                await ReviewBranchHandoffAsync();
                break;
        }
    }

    private async Task StartPlanningAsync(GoalView goal)
    {
        if (store.Current.Settings.AgentDefaults is not { Models.Count: > 0 })
        {
            await store.DiscoverAgentDefaultsAsync(cancellationToken);
        }

        AgentDefaultsSnapshot? defaults = store.Current.Settings.AgentDefaults;
        GoalModelCandidate[] candidates = ModelSelectionCatalog.ForRole(
            defaults?.Models ?? [], AgentRole.Lead);
        GoalModelSelectionView? effective = store.Current.Goals.ModelSelections
            .FirstOrDefault(selection => selection.Role is AgentRole.Lead);
        AgentRoleDefault? configured = defaults?.Roles
            .FirstOrDefault(roleDefault => roleDefault.Role is AgentRole.Lead);
        GoalModelCandidate? preferred = candidates.FirstOrDefault(candidate =>
            candidate.Provider == effective?.Provider && candidate.Model == effective?.Model) ??
            candidates.FirstOrDefault(candidate =>
                candidate.Provider == configured?.Provider && candidate.Model == configured?.Model);
        PlanGenerationDialog dialog = new(
            candidates,
            preferred,
            GoalPresentationFormatter.StartDisclosure(store.Current.Goals));
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } result)
        {
            return;
        }

        if (result.LeadModel.Access is ModelAccess.Remote &&
            !await new RemoteModelAuthorizationDialog(
                    goal,
                    result.LeadModel,
                    AgentRole.Lead)
                .ShowDialog<bool>(this))
        {
            return;
        }

        await store.StartGoalWorkflowAsync(
            goal.Id,
            result.LeadModel,
            cancellationToken);
    }

    private async Task WritePlanAsync(GoalView goal)
    {
        TextEntryDialog dialog = new(
            "Write plan manually",
            "Plan content",
            "Save plan",
            "A plan is required.");
        await dialog.ShowDialog(this);
        if (dialog.Result is { } content)
        {
            await store.ProposePlanAsync(goal.Id, content, cancellationToken);
        }
    }

    private async Task ApprovePlanAsync(GoalView goal)
    {
        if (store.Current.Goals.CurrentPlan is not { } plan)
        {
            return;
        }

        PlanApprovalDialog dialog = new(goal, plan);
        if (await dialog.ShowDialog<bool>(this))
        {
            await store.DecidePlanAsync(
                goal.Id,
                PlanDecision.Approve,
                reason: null,
                cancellationToken);
        }
    }

    private async Task RequestPlanChangesAsync(GoalView goal)
    {
        TextEntryDialog dialog = new(
            "Request plan changes",
            "Required reason",
            "Request changes",
            "A reason is required.");
        await dialog.ShowDialog(this);
        if (dialog.Result is { } reason)
        {
            await store.DecidePlanAsync(
                goal.Id,
                PlanDecision.Deny,
                reason,
                cancellationToken);
        }
    }

    private async Task ContinueRunAsync(GoalView goal)
    {
        await store.ResumeGoalWorkflowAsync(goal.Id, cancellationToken);
    }

    private async Task RetryRunAsync(GoalView goal)
    {
        if (store.Current.Goals.Workflow?.RetryRole is not { } retryRole)
        {
            return;
        }

        AgentRole role = retryRole switch
        {
            GoalWorkflowRetryRole.Lead => AgentRole.Lead,
            GoalWorkflowRetryRole.Implementer => AgentRole.Implementer,
            GoalWorkflowRetryRole.Reviewer => AgentRole.Reviewer,
            _ => throw new ArgumentOutOfRangeException(nameof(retryRole)),
        };
        if (store.Current.Settings.AgentDefaults is not { Models.Count: > 0 })
        {
            await store.DiscoverAgentDefaultsAsync(cancellationToken);
        }

        AgentDefaultsSnapshot? defaults = store.Current.Settings.AgentDefaults;
        GoalModelCandidate[] candidates = ModelSelectionCatalog.ForRole(
            defaults?.Models ?? [], role);
        GoalModelSelectionView? effective = store.Current.Goals.ModelSelections
            .FirstOrDefault(selection => selection.Role == role);
        AgentRoleDefault? configured = defaults?.Roles
            .FirstOrDefault(roleDefault => roleDefault.Role == role);
        GoalModelCandidate? preferred = candidates.FirstOrDefault(candidate =>
            candidate.Provider == effective?.Provider && candidate.Model == effective?.Model) ??
            candidates.FirstOrDefault(candidate =>
                candidate.Provider == configured?.Provider && candidate.Model == configured?.Model);
        WorkflowRetryDialog dialog = new(
            retryRole,
            candidates,
            preferred,
            GoalPresentationFormatter.RetryDisclosure(retryRole, store.Current.Goals));
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } result)
        {
            return;
        }

        if (result.Model.Access is ModelAccess.Remote &&
            !await new RemoteModelAuthorizationDialog(goal, result.Model, role)
                .ShowDialog<bool>(this))
        {
            return;
        }

        await store.RetryGoalWorkflowAsync(
            goal.Id,
            retryRole,
            result.Model,
            result.Guidance is null ? null : new(result.Guidance),
            cancellationToken);
    }

    private async Task AbortGoalAsync(GoalView goal)
    {
        AbortGoalDialog dialog = new(goal);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } reason)
        {
            return;
        }

        await store.AbortGoalAsync(goal.Id, reason, cancellationToken);
        if (store.Current.Goals.SelectedGoalId is null)
        {
            composer.Focus();
        }
    }

    private async Task ReviewBranchHandoffAsync()
    {
        if (workbench is null || !workbench.ShowGit())
        {
            return;
        }

        await workbench.RefreshGitAsync();
    }

    private async Task ApproveRestoreAsync(GoalView goal, ConversationWorkflowCard card)
    {
        CapabilityApprovalView? approval = RestoreApproval(card);
        if (approval is null)
        {
            return;
        }

        RestoreDecisionConfirmationDialog confirmation = new(approval);
        if (await confirmation.ShowDialog<bool>(this))
        {
            await store.DecideRestoreApprovalAsync(
                goal.Id,
                approval.Id,
                CapabilityDecision.Approve,
                reason: null,
                cancellationToken);
        }
    }

    private async Task DenyRestoreAsync(GoalView goal, ConversationWorkflowCard card)
    {
        CapabilityApprovalView? approval = RestoreApproval(card);
        if (approval is null)
        {
            return;
        }

        TextEntryDialog dialog = new(
            "Deny restore request",
            "Required reason",
            "Deny request",
            "A denial reason is required.");
        await dialog.ShowDialog(this);
        if (dialog.Result is { } reason)
        {
            await store.DecideRestoreApprovalAsync(
                goal.Id,
                approval.Id,
                CapabilityDecision.Deny,
                reason,
                cancellationToken);
        }
    }

    private CapabilityApprovalView? RestoreApproval(ConversationWorkflowCard card) =>
        store.Current.Goals.CapabilityApprovals.FirstOrDefault(approval =>
            card.Id == $"capability.{approval.Id.Value}" &&
            approval.Capability is CapabilityKind.Restore &&
            approval.State is CapabilityApprovalState.Pending);

    private async Task DecideCommitAsync(bool resuming)
    {
        if (store.Current.Goals.CommitApproval is not { } approval)
        {
            return;
        }

        ExactCommitConfirmationDialog confirmation = new(approval, resuming);
        if (await confirmation.ShowDialog<bool>(this))
        {
            await store.DecideCommitAsync(
                GoalCommitDecision.Approve,
                reason: null,
                cancellationToken);
        }
    }

    private async Task DenyCommitAsync()
    {
        TextEntryDialog dialog = new(
            "Deny exact commit",
            "Required reason",
            "Deny commit",
            "A denial reason is required.");
        await dialog.ShowDialog(this);
        if (dialog.Result is { } reason)
        {
            await store.DecideCommitAsync(
                GoalCommitDecision.Deny,
                new GoalCommitDecisionReason(reason),
                cancellationToken);
        }
    }

}
