using System.Globalization;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed class GoalDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly ListBox goals = new();
    private readonly TextBlock goalDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox plan = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox routeCost = ReadOnlyViewer();
    private readonly TextBox workflowDetails = ReadOnlyViewer();
    private readonly Button create = new() { Content = "New goal" };
    private readonly Button propose = new() { Content = "Propose plan" };
    private readonly Button approve = new() { Content = "Approve plan…" };
    private readonly Button deny = new() { Content = "Deny plan…" };
    private readonly Button models = new() { Content = "Role models…" };
    private readonly Button context = new() { Content = "Semantic context…" };
    private readonly Button commit = new() { Content = "Exact commit…" };
    private readonly Button restoreApprovals = new() { Content = "Restore approvals…" };
    private readonly Button startRun = new() { Content = "Start planning…" };
    private readonly Button resumeRun = new() { Content = "Continue run…" };
    private readonly Button cancelRun = new() { Content = "Cancel run" };
    private readonly Button refresh = new() { Content = "Refresh" };
    private bool rendering;

    internal GoalDialog(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        Title = "Goals and plans";
        Width = 1040;
        Height = 720;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state)));
        Closed += (_, _) => subscription.Dispose();
        Opened += async (_, _) => await store.RefreshGoalsAsync(cancellationToken);
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            ColumnDefinitions = new("300,*"),
            RowDefinitions = new("*,Auto,Auto"),
            Margin = new(20),
            ColumnSpacing = 16,
            RowSpacing = 12,
        };
        StackPanel left = new()
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Workspace goals",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                },
                goals,
            },
        };
        goals.MinHeight = 420;
        AutomationProperties.SetName(goals, "Workspace goals");
        AutomationProperties.SetName(goalDetails, "Selected goal details");
        AutomationProperties.SetName(status, "Goal operation status");
        root.Children.Add(left);

        Grid details = new()
        {
            RowDefinitions = new("Auto,*"),
            RowSpacing = 10,
        };
        details.Children.Add(new TextBlock
        {
            Text = "Goal details",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });
        AutomationProperties.SetName(plan, "Current plan content");
        AutomationProperties.SetName(routeCost, "Role routes and remote cost");
        AutomationProperties.SetName(workflowDetails, "Production workflow details");
        TabControl tabs = new()
        {
            ItemsSource = new TabItem[]
            {
                new() { Header = "Overview", Content = new ScrollViewer { Content = goalDetails } },
                new() { Header = "Plan", Content = plan },
                new() { Header = "Models & cost", Content = routeCost },
                new() { Header = "Run & evidence", Content = workflowDetails },
            },
        };
        Grid.SetRow(tabs, 1);
        details.Children.Add(tabs);
        Grid.SetColumn(details, 1);
        root.Children.Add(details);

        WrapPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                create,
                propose,
                approve,
                deny,
                models,
                context,
                commit,
                restoreApprovals,
                startRun,
                resumeRun,
                cancelRun,
                refresh,
            },
        };
        Grid.SetRow(actions, 1);
        Grid.SetColumnSpan(actions, 2);
        root.Children.Add(actions);

        Grid footer = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        footer.Children.Add(status);
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 2);
        Grid.SetColumnSpan(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void WireInteractions()
    {
        goals.SelectionChanged += async (_, _) =>
        {
            if (!rendering && goals.SelectedItem is GoalChoice choice)
            {
                await store.SelectGoalAsync(choice.Goal.Id, cancellationToken);
            }
        };
        refresh.Click += async (_, _) => await store.RefreshGoalsAsync(cancellationToken);
        create.Click += async (_, _) =>
        {
            WorkspaceView? workspace = store.Current.Workspaces.Registered
                .FirstOrDefault(item => item.IsActive);
            if (workspace is null)
            {
                return;
            }

            NewGoalDialog dialog = new(
                workspace.Id,
                store.Current.Settings.RemoteSpendPreference);
            await dialog.ShowDialog(this);
            if (dialog.Result is not null)
            {
                await store.CreateGoalAsync(dialog.Result, cancellationToken);
            }
        };
        propose.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

            TextEntryDialog dialog = new(
                "Propose plan",
                "Plan content",
                "Save plan",
                "A plan is required.");
            await dialog.ShowDialog(this);
            if (dialog.Result is not null)
            {
                await store.ProposePlanAsync(selected.Id, dialog.Result, cancellationToken);
            }
        };
        approve.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            PlanView? currentPlan = store.Current.Goals.CurrentPlan;
            if (selected is null || currentPlan is null)
            {
                return;
            }

            PlanApprovalDialog dialog = new(selected, currentPlan);
            bool confirmed = await dialog.ShowDialog<bool>(this);
            if (confirmed)
            {
                await store.DecidePlanAsync(
                    selected.Id,
                    PlanDecision.Approve,
                    reason: null,
                    cancellationToken);
            }
        };
        deny.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

            TextEntryDialog dialog = new(
                "Deny plan",
                "Required reason",
                "Deny plan",
                "A denial reason is required.");
            await dialog.ShowDialog(this);
            if (dialog.Result is not null)
            {
                await store.DecidePlanAsync(
                    selected.Id,
                    PlanDecision.Deny,
                    dialog.Result,
                    cancellationToken);
            }
        };
        models.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

            await store.DiscoverGoalModelsAsync(selected.Id, cancellationToken);
            if (store.Current.Goals.ModelCatalog is not null)
            {
                ModelRoutingDialog dialog = new(store, selected, cancellationToken);
                await dialog.ShowDialog(this);
            }
        };
        context.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

            await store.RefreshSemanticStatusAsync(selected.Id, cancellationToken);
            SemanticContextDialog dialog = new(store, selected, cancellationToken);
            await dialog.ShowDialog(this);
        };
        commit.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

            await store.RefreshCommitAsync(selected.Id, cancellationToken);
            if (store.Current.Goals.CommitPreview is not null ||
                store.Current.Goals.CommitApproval is not null)
            {
                CommitApprovalDialog dialog = new(store, cancellationToken);
                await dialog.ShowDialog(this);
            }
        };
        restoreApprovals.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

            await store.RefreshCapabilityApprovalsAsync(selected.Id, cancellationToken);
            RestoreApprovalDialog dialog = new(store, selected, cancellationToken);
            await dialog.ShowDialog(this);
        };
        startRun.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

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
                        selected,
                        result.LeadModel,
                        AgentRole.Lead)
                    .ShowDialog<bool>(this))
            {
                return;
            }

            await store.StartGoalWorkflowAsync(
                selected.Id,
                result.LeadModel,
                cancellationToken);
        };
        resumeRun.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

            await store.ResumeGoalWorkflowAsync(selected.Id, cancellationToken);
        };
        cancelRun.Click += (_, _) => store.CancelGoalWorkflow();
    }

    private void Render(AvaloniaShellState state)
    {
        rendering = true;
        try
        {
            GoalChoice[] choices = state.Goals.Items.Select(goal => new GoalChoice(goal)).ToArray();
            goals.ItemsSource = choices;
            goals.SelectedItem = choices.FirstOrDefault(choice =>
                choice.Goal.Id == state.Goals.SelectedGoalId);

            GoalView? selected = state.Goals.SelectedGoal;
            PlanView? currentPlan = state.Goals.CurrentPlan;
            goalDetails.Text = selected is null
                ? "Select a goal to inspect its objective, limits, and plan."
                : FormatGoal(selected);
            plan.Text = currentPlan is null
                ? "No plan has been proposed."
                : $"Revision {currentPlan.Revision.Value} — {currentPlan.State}\n\n{currentPlan.Content}";
            routeCost.Text = GoalPresentationFormatter.FormatRoutesAndCost(
                selected,
                state.Goals.ModelSelections,
                state.Goals.Cost);
            workflowDetails.Text = GoalPresentationFormatter.FormatWorkflow(state.Goals.Workflow);

            WorkspaceView? activeWorkspace = state.Workspaces.Registered
                .FirstOrDefault(workspace => workspace.IsActive);
            bool busy = state.Goals.IsBusy;
            create.IsEnabled = !busy && activeWorkspace is not null;
            propose.IsEnabled = !busy && selected?.State is GoalState.Draft or GoalState.NeedsPlanRevision;
            bool awaitingDecision = selected?.State is GoalState.AwaitingPlanApproval &&
                                    currentPlan?.State is PlanState.Pending;
            approve.IsEnabled = !busy && awaitingDecision && activeWorkspace?.IsTrusted is true;
            deny.IsEnabled = !busy && awaitingDecision;
            models.IsEnabled = !busy && selected is not null;
            context.IsEnabled = !busy && selected is not null && activeWorkspace?.IsTrusted is true;
            commit.IsEnabled = !busy &&
                               selected?.State is GoalState.Approved &&
                               state.Goals.Workflow is not null;
            restoreApprovals.IsEnabled = !busy &&
                                         selected?.State is GoalState.Approved &&
                                         activeWorkspace?.IsTrusted is true;
            startRun.IsEnabled = !busy &&
                                 selected?.State is GoalState.Draft or GoalState.NeedsPlanRevision;
            resumeRun.IsEnabled = !busy &&
                                  selected?.State is GoalState.Approved &&
                                  state.Goals.Workflow?.CanResume is true;
            cancelRun.IsVisible = state.Goals.IsWorkflowRunning;
            cancelRun.IsEnabled = state.Goals.IsWorkflowRunning;
            refresh.IsEnabled = !busy;

            string trustHint = awaitingDecision && activeWorkspace?.IsTrusted is false
                ? " Trust the active workspace before approving this plan."
                : string.Empty;
            status.Text = (busy ? "Working…" : state.Goals.Status ?? string.Empty) + trustHint;
        }
        finally
        {
            rendering = false;
        }
    }

    private static string FormatGoal(GoalView goal)
    {
        RemoteSpendPreference spend = RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget);
        string budget = spend.Mode switch
        {
            RemoteSpendMode.Unlimited => "Remote spend: Unlimited",
            RemoteSpendMode.Capped => $"Remote cap: {(spend.Cap!.Value / 1_000_000m):C6}",
            RemoteSpendMode.LocalOnly => "Local models only",
            _ => throw new ArgumentOutOfRangeException(),
        };
        return $"{goal.Title}\nState: {goal.State}\nReview-cycle limit: " +
               $"{goal.ReviewCycleLimit.Value}\n{budget}\n\n{goal.Objective}";
    }

    private sealed record GoalChoice(GoalView Goal)
    {
        public override string ToString() => $"{Goal.Title} — {Goal.State}";
    }

    private static TextBox ReadOnlyViewer() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
    };
}
