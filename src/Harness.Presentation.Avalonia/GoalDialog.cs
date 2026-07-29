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

            NewGoalDialog dialog = new(workspace.Id);
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

            string disclosure = GoalPresentationFormatter.StartDisclosure(store.Current.Goals);
            OutputLimitsDialog dialog = new(
                "Start Lead planning",
                ["Lead maximum output tokens"],
                disclosure);
            await dialog.ShowDialog(this);
            if (dialog.Result is { Length: 1 } limits)
            {
                await store.StartGoalWorkflowAsync(
                    selected.Id,
                    new(limits[0]),
                    cancellationToken);
            }
        };
        resumeRun.Click += async (_, _) =>
        {
            GoalView? selected = store.Current.Goals.SelectedGoal;
            if (selected is null)
            {
                return;
            }

            string disclosure = GoalPresentationFormatter.ResumeDisclosure(
                selected,
                store.Current.Goals);
            OutputLimitsDialog dialog = new(
                "Continue production run",
                ["Implementer maximum output tokens", "Reviewer maximum output tokens"],
                disclosure);
            await dialog.ShowDialog(this);
            if (dialog.Result is { Length: 2 } limits)
            {
                await store.ResumeGoalWorkflowAsync(
                    selected.Id,
                    new(limits[0]),
                    new(limits[1]),
                    cancellationToken);
            }
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
        string budget = goal.RemoteBudget is null
            ? "Local models only"
            : $"Remote cap: {(goal.RemoteBudget.Value / 1_000_000m):C6}";
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

internal sealed class NewGoalDialog : Window
{
    private readonly string workspaceId;
    private readonly TextBox title = new();
    private readonly TextBox objective = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 140,
    };
    private readonly TextBox reviewLimit = new() { Text = "3" };
    private readonly TextBox remoteBudget = new();
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };

    internal NewGoalDialog(string workspaceId)
    {
        this.workspaceId = workspaceId;
        Title = "New goal";
        Width = 680;
        Height = 560;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
    }

    internal GoalCreateRequest? Result { get; private set; }

    private Control BuildContent()
    {
        AutomationProperties.SetName(title, "Goal title");
        AutomationProperties.SetName(objective, "Goal objective");
        AutomationProperties.SetName(reviewLimit, "Review-cycle limit");
        AutomationProperties.SetName(remoteBudget, "Remote budget in USD");
        AutomationProperties.SetName(validation, "New goal validation");
        StackPanel panel = new() { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Title" });
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = "Objective" });
        panel.Children.Add(objective);
        panel.Children.Add(new TextBlock { Text = "Review-cycle limit (1–20)" });
        panel.Children.Add(reviewLimit);
        panel.Children.Add(new TextBlock { Text = "Remote budget USD (blank means local only)" });
        panel.Children.Add(remoteBudget);
        panel.Children.Add(validation);

        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button save = new() { Content = "Create goal" };
        save.Click += (_, _) => Save();
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, save },
        });
        return panel;
    }

    private void Save()
    {
        if (!int.TryParse(reviewLimit.Text, NumberStyles.None, CultureInfo.InvariantCulture,
                out int cycles) || cycles is < 1 or > 20)
        {
            validation.Text = "Review-cycle limit must be an integer from 1 through 20.";
            return;
        }

        if (!TryParseBudget(remoteBudget.Text, out MicroUsdAmount? budget, out string? error))
        {
            validation.Text = error;
            return;
        }

        Result = new(
            workspaceId,
            title.Text ?? string.Empty,
            objective.Text ?? string.Empty,
            new(cycles),
            budget);
        Close();
    }

    private static bool TryParseBudget(
        string? value,
        out MicroUsdAmount? budget,
        out string? error)
    {
        string input = value?.Trim() ?? string.Empty;
        if (input.Length == 0)
        {
            budget = null;
            error = null;
            return true;
        }

        if (!decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture,
                out decimal usd) || usd <= 0)
        {
            budget = null;
            error = "Remote budget must be a positive USD amount using '.' as the decimal separator.";
            return false;
        }

        decimal microUsd = usd * 1_000_000m;
        if (microUsd != decimal.Truncate(microUsd) || microUsd > long.MaxValue)
        {
            budget = null;
            error = "Remote budget supports at most six decimal places and must fit the supported range.";
            return false;
        }

        budget = new((long)microUsd);
        error = null;
        return true;
    }
}

internal sealed class TextEntryDialog : Window
{
    private readonly TextBox editor = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 260,
    };
    private readonly TextBlock validation = new();
    private readonly string requiredMessage;

    internal TextEntryDialog(
        string title,
        string label,
        string action,
        string requiredMessage)
    {
        this.requiredMessage = requiredMessage;
        Title = title;
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(editor, label);
        AutomationProperties.SetName(validation, $"{title} validation");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button save = new() { Content = action };
        save.Click += (_, _) => Save();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
                editor,
                validation,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, save },
                },
            },
        };
    }

    internal string? Result { get; private set; }

    private void Save()
    {
        string content = editor.Text?.Trim() ?? string.Empty;
        if (content.Length == 0)
        {
            validation.Text = requiredMessage;
            return;
        }

        Result = content;
        Close();
    }
}

internal sealed class PlanApprovalDialog : Window
{
    internal PlanApprovalDialog(GoalView goal, PlanView plan)
    {
        Title = "Approve plan and capabilities";
        Width = 680;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        TextEditor planContent = CodeEditorView.Create(
            plan.Content,
            wordWrap: true,
            showLineNumbers: false);
        planContent.MinHeight = 260;
        AutomationProperties.SetName(
            planContent,
            $"Plan revision {plan.Revision.Value} content");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button approve = new() { Content = "Approve and create worktree" };
        approve.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"Approve {goal.Title} — plan revision {plan.Revision.Value}?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Approval creates an isolated branch and worktree and grants the goal " +
                           "repository-local inspection, edit, build, and test capabilities. " +
                           "Restore, network access, destructive actions, and commits remain " +
                           "separately approval-gated.",
                    TextWrapping = TextWrapping.Wrap,
                },
                planContent,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, approve },
                },
            },
        };
    }
}

internal sealed class ModelRoutingDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly GoalView goal;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly ListBox candidates = new();
    private readonly TextBox selections = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 100,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button lead = new() { Content = "Use for Lead" };
    private readonly Button implementer = new() { Content = "Use for Implementer" };
    private readonly Button reviewer = new() { Content = "Use for Reviewer" };

    internal ModelRoutingDialog(
        AvaloniaPresentationStore store,
        GoalView goal,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.goal = goal;
        this.cancellationToken = cancellationToken;
        Title = "Goal role models";
        Width = 900;
        Height = 680;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Goals)));
        Closed += (_, _) => subscription.Dispose();
    }

    private Control BuildContent()
    {
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        return new Grid
        {
            RowDefinitions = new("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = "Per-role model routing",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                },
                AtRow(new TextBlock
                {
                    Text = "Catalog discovery performs no inference. Remote selections authorize only " +
                           "the selected provider/model for this goal and remain bounded by its cap.",
                    TextWrapping = TextWrapping.Wrap,
                }, 1),
                AtRow(candidates, 2),
                AtRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { lead, implementer, reviewer },
                }, 3),
                AtRow(selections, 4),
                AtRow(new Grid
                {
                    ColumnDefinitions = new("*,Auto"),
                    Children = { status, AtColumn(close, 1) },
                }, 5),
            },
        };
    }

    private void WireInteractions()
    {
        lead.Click += async (_, _) => await SelectAsync(AgentRole.Lead);
        implementer.Click += async (_, _) => await SelectAsync(AgentRole.Implementer);
        reviewer.Click += async (_, _) => await SelectAsync(AgentRole.Reviewer);
    }

    private async Task SelectAsync(AgentRole role)
    {
        if (candidates.SelectedItem is not ModelChoice choice)
        {
            status.Text = "Select a model.";
            return;
        }

        GoalModelCandidate candidate = choice.Candidate;
        if (candidate.Access is ModelAccess.Remote)
        {
            if (goal.RemoteBudget is null)
            {
                status.Text = "This goal is local-only. Create a capped goal to authorize remote models.";
                return;
            }

            RemoteModelAuthorizationDialog confirmation = new(goal, candidate, role);
            if (!await confirmation.ShowDialog<bool>(this))
            {
                return;
            }
        }

        await store.SelectGoalModelAsync(goal.Id, role, candidate, cancellationToken);
    }

    private void Render(GoalManagementState state)
    {
        GoalModelCatalog? catalog = state.ModelCatalog;
        ModelChoice[] items = catalog?.Models.Select(model => new ModelChoice(model)).ToArray() ?? [];
        string? selectedKey = (candidates.SelectedItem as ModelChoice)?.Key;
        candidates.ItemsSource = items;
        candidates.SelectedItem = items.FirstOrDefault(item => item.Key == selectedKey) ?? items.FirstOrDefault();
        selections.Text = GoalPresentationFormatter.FormatSelections(state.ModelSelections);
        bool enabled = !state.IsBusy && items.Length > 0;
        lead.IsEnabled = enabled;
        implementer.IsEnabled = enabled;
        reviewer.IsEnabled = enabled;
        status.Text = state.IsBusy
            ? "Working…"
            : state.Status ?? catalog?.Error ?? string.Empty;
    }

    private static T AtRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T AtColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private sealed record ModelChoice(GoalModelCandidate Candidate)
    {
        internal string Key => $"{Candidate.Provider.Value}/{Candidate.Model.Value}";

        public override string ToString() => GoalPresentationFormatter.FormatCandidate(Candidate);
    }
}

internal sealed class RemoteModelAuthorizationDialog : Window
{
    internal RemoteModelAuthorizationDialog(
        GoalView goal,
        GoalModelCandidate candidate,
        AgentRole role)
    {
        Title = "Authorize remote model";
        Width = 620;
        Height = 340;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        string pricing = candidate.InputPrice is null || candidate.OutputPrice is null
            ? "Published pricing is unavailable. Inference will fail closed until pricing is known."
            : $"Published rates: input ${candidate.InputPrice.Value:0.######}/M tokens, " +
              $"output ${candidate.OutputPrice.Value:0.######}/M tokens" +
              (candidate.RequestPrice?.Value > 0
                  ? $", request ${candidate.RequestPrice.Value:0.######}."
                  : ".");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button authorize = new() { Content = "Authorize selection" };
        authorize.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = $"Use {candidate.Provider.Value}/{candidate.Model.Value} for {role}?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Goal cap: ${GoalPresentationFormatter.ToUsd(goal.RemoteBudget!.Value)}. " + pricing,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Every request reserves a conservative maximum before inference, is " +
                           "attributed to this goal, and fails closed when budget or pricing is unavailable.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, authorize },
                },
            },
        };
    }
}

internal sealed class OutputLimitsDialog : Window
{
    private readonly TextBox[] fields;
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };

    internal OutputLimitsDialog(
        string title,
        IReadOnlyList<string> labels,
        string disclosure)
    {
        Title = title;
        Width = 720;
        Height = 470 + (labels.Count * 60);
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        fields = labels.Select(_ => new TextBox { Text = "2048" }).ToArray();
        StackPanel panel = new() { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Bounded model execution",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });
        panel.Children.Add(new TextBlock { Text = disclosure, TextWrapping = TextWrapping.Wrap });
        foreach ((string label, TextBox field) in labels.Zip(fields))
        {
            panel.Children.Add(new TextBlock { Text = label });
            panel.Children.Add(field);
        }

        panel.Children.Add(validation);
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button run = new() { Content = "Run with these limits" };
        run.Click += (_, _) => Save();
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, run },
        });
        Content = new ScrollViewer { Content = panel };
    }

    internal int[]? Result { get; private set; }

    private void Save()
    {
        int[] values = fields.Select(field =>
                int.TryParse(field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                    ? value
                    : 0)
            .ToArray();
        if (values.Any(value => value is < 1 or > 8192))
        {
            validation.Text = "Every output maximum must be an integer from 1 through 8192 tokens.";
            return;
        }

        Result = values;
        Close();
    }
}

internal sealed class RestoreApprovalDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly GoalView goal;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly ListBox approvals = new();
    private readonly TextBox details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button request = new() { Content = "New correlated request…" };
    private readonly Button approve = new() { Content = "Approve once…" };
    private readonly Button deny = new() { Content = "Deny…" };
    private bool rendering;

    internal RestoreApprovalDialog(
        AvaloniaPresentationStore store,
        GoalView goal,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.goal = goal;
        this.cancellationToken = cancellationToken;
        Title = "Restore approvals";
        Width = 900;
        Height = 650;
        MinWidth = 700;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Goals)));
        Closed += (_, _) => subscription.Dispose();
    }

    private Control BuildContent()
    {
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        Grid root = new()
        {
            ColumnDefinitions = new("320,*"),
            RowDefinitions = new("Auto,*,Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 10,
            Margin = new Thickness(20),
        };
        TextBlock heading = new()
        {
            Text = "Correlation-bound restore authorization",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        };
        Grid.SetColumnSpan(heading, 2);
        root.Children.Add(heading);
        Grid.SetRow(approvals, 1);
        root.Children.Add(approvals);
        Grid.SetRow(details, 1);
        Grid.SetColumn(details, 1);
        root.Children.Add(details);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { request, approve, deny },
        };
        Grid.SetRow(actions, 2);
        Grid.SetColumnSpan(actions, 2);
        root.Children.Add(actions);
        Grid footer = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(status);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 3);
        Grid.SetColumnSpan(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void WireInteractions()
    {
        approvals.SelectionChanged += (_, _) =>
        {
            if (!rendering && approvals.SelectedItem is RestoreApprovalChoice choice)
            {
                details.Text = Format(choice.Approval);
                SetDecisionButtons(choice.Approval, store.Current.Goals.IsBusy);
            }
        };
        request.Click += async (_, _) =>
        {
            RestoreRequestDialog dialog = new();
            await dialog.ShowDialog(this);
            if (dialog.Result is { } result)
            {
                await store.RequestRestoreApprovalAsync(
                    goal.Id,
                    result.CorrelationId,
                    result.Rationale,
                    cancellationToken);
            }
        };
        approve.Click += async (_, _) =>
        {
            if (approvals.SelectedItem is not RestoreApprovalChoice choice)
            {
                return;
            }

            RestoreDecisionConfirmationDialog confirmation = new(choice.Approval);
            if (await confirmation.ShowDialog<bool>(this))
            {
                await store.DecideRestoreApprovalAsync(
                    goal.Id,
                    choice.Approval.Id,
                    CapabilityDecision.Approve,
                    reason: null,
                    cancellationToken);
            }
        };
        deny.Click += async (_, _) =>
        {
            if (approvals.SelectedItem is not RestoreApprovalChoice choice)
            {
                return;
            }

            TextEntryDialog reason = new(
                "Deny restore request",
                "Required reason",
                "Deny request",
                "A denial reason is required.");
            await reason.ShowDialog(this);
            if (reason.Result is not null)
            {
                await store.DecideRestoreApprovalAsync(
                    goal.Id,
                    choice.Approval.Id,
                    CapabilityDecision.Deny,
                    reason.Result,
                    cancellationToken);
            }
        };
    }

    private void Render(GoalManagementState state)
    {
        rendering = true;
        try
        {
            RestoreApprovalChoice[] items = state.CapabilityApprovals
                .Select(item => new RestoreApprovalChoice(item))
                .ToArray();
            string? selectedId = (approvals.SelectedItem as RestoreApprovalChoice)?.Approval.Id.Value;
            approvals.ItemsSource = items;
            approvals.SelectedItem = items.FirstOrDefault(item =>
                item.Approval.Id.Value == selectedId) ?? items.FirstOrDefault();
            CapabilityApprovalView? selected =
                (approvals.SelectedItem as RestoreApprovalChoice)?.Approval;
            details.Text = selected is null
                ? "No restore approval requests. Create one only for a known restore correlation."
                : Format(selected);
            request.IsEnabled = !state.IsBusy;
            SetDecisionButtons(selected, state.IsBusy);
            status.Text = state.IsBusy ? "Updating durable approval…" : state.Status ?? string.Empty;
        }
        finally
        {
            rendering = false;
        }
    }

    private void SetDecisionButtons(CapabilityApprovalView? approval, bool busy)
    {
        bool pending = approval?.State is CapabilityApprovalState.Pending;
        approve.IsEnabled = !busy && pending;
        deny.IsEnabled = !busy && pending;
    }

    private static string Format(CapabilityApprovalView approval) => string.Join(
        '\n',
        $"State: {approval.State}",
        $"Capability: {approval.Capability}",
        $"Goal: {approval.GoalId}",
        $"Correlation: {approval.CorrelationId.Value}",
        $"Exact target: {approval.Target}",
        $"Requested: {approval.RequestedAt:O}",
        approval.DecidedAt is null ? string.Empty : $"Decided: {approval.DecidedAt:O}",
        string.Empty,
        "Rationale:",
        approval.Rationale,
        approval.DecisionReason is null ? string.Empty : $"\nDecision reason:\n{approval.DecisionReason}");

    private sealed record RestoreApprovalChoice(CapabilityApprovalView Approval)
    {
        public override string ToString() =>
            $"{Approval.State} — {Approval.CorrelationId.Value}";
    }
}

internal sealed class RestoreRequestDialog : Window
{
    private readonly TextBox correlation = new();
    private readonly TextBox rationale = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 150,
    };
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };

    internal RestoreRequestDialog()
    {
        Title = "Request one restore authorization";
        Width = 680;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        correlation.Text = Guid.NewGuid().ToString("N");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button save = new() { Content = "Record pending request" };
        save.Click += (_, _) => Save();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Restore requires a unique correlation shared by exactly one later restore " +
                           "tool call. It does not authorize other correlations, targets, or capabilities.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = "Correlation identifier" },
                correlation,
                new TextBlock { Text = "Why is dependency restore required?" },
                rationale,
                validation,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, save },
                },
            },
        };
    }

    internal RestoreRequestInput? Result { get; private set; }

    private void Save()
    {
        string correlationValue = correlation.Text?.Trim() ?? string.Empty;
        string rationaleValue = rationale.Text?.Trim() ?? string.Empty;
        if (correlationValue.Length == 0 || rationaleValue.Length == 0)
        {
            validation.Text = "Correlation and rationale are required.";
            return;
        }

        Result = new(new(correlationValue), rationaleValue);
        Close();
    }
}

internal sealed record RestoreRequestInput(
    ToolCorrelationId CorrelationId,
    string Rationale);

internal sealed class RestoreDecisionConfirmationDialog : Window
{
    internal RestoreDecisionConfirmationDialog(CapabilityApprovalView approval)
    {
        Title = "Approve one restore";
        Width = 680;
        Height = 410;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button approve = new() { Content = "Approve this restore once" };
        approve.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Approve this exact network-capable restore request?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Goal: {approval.GoalId}\n" +
                           $"Correlation: {approval.CorrelationId.Value}\n" +
                           $"Target: {approval.Target}\n\nRationale:\n{approval.Rationale}",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Approval is durable but valid only for this goal, correlation, Restore " +
                           "capability, and registered entry point. It does not approve package changes " +
                           "or any other network operation.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, approve },
                },
            },
        };
    }
}

internal sealed class CommitApprovalDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly TextBox fingerprint = Viewer();
    private readonly TextEditor diff = CodeEditorView.Create();
    private readonly TextBox message = new();
    private readonly TextBox authorName = new();
    private readonly TextBox authorEmail = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel requestFields = new() { Spacing = 6 };
    private readonly Button request = new() { Content = "Record pending request" };
    private readonly Button approve = new() { Content = "Approve exact diff…" };
    private readonly Button deny = new() { Content = "Deny…" };
    private readonly Button resume = new() { Content = "Resume approved commit…" };

    internal CommitApprovalDialog(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        Title = "Exact commit approval";
        Width = 1040;
        Height = 760;
        MinWidth = 800;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Goals)));
        Closed += (_, _) => subscription.Dispose();
    }

    private Control BuildContent()
    {
        requestFields.Children.Add(new TextBlock { Text = "Commit message" });
        requestFields.Children.Add(message);
        requestFields.Children.Add(new TextBlock { Text = "Author name" });
        requestFields.Children.Add(authorName);
        requestFields.Children.Add(new TextBlock { Text = "Author email" });
        requestFields.Children.Add(authorEmail);
        requestFields.Children.Add(request);

        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        Grid root = new()
        {
            RowDefinitions = new("Auto,120,*,Auto,Auto,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(20),
        };
        root.Children.Add(new TextBlock
        {
            Text = "User-owned exact-diff commit",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });
        Grid.SetRow(fingerprint, 1);
        root.Children.Add(fingerprint);
        Grid.SetRow(diff, 2);
        AutomationProperties.SetName(diff, "Complete commit diff");
        root.Children.Add(diff);
        Grid.SetRow(requestFields, 3);
        root.Children.Add(requestFields);
        StackPanel decisions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { approve, deny, resume },
        };
        Grid.SetRow(decisions, 4);
        root.Children.Add(decisions);
        Grid footer = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(status);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 5);
        root.Children.Add(footer);
        return root;
    }

    private void WireInteractions()
    {
        request.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(message.Text) ||
                string.IsNullOrWhiteSpace(authorName.Text) ||
                string.IsNullOrWhiteSpace(authorEmail.Text))
            {
                status.Text = "Commit message, author name, and author email are required.";
                return;
            }

            await store.RequestCommitApprovalAsync(
                new(message.Text),
                new(authorName.Text),
                new(authorEmail.Text),
                cancellationToken);
        };
        approve.Click += async (_, _) => await ConfirmAndCommitAsync(resuming: false);
        resume.Click += async (_, _) => await ConfirmAndCommitAsync(resuming: true);
        deny.Click += async (_, _) =>
        {
            TextEntryDialog reason = new(
                "Deny exact commit",
                "Required reason",
                "Deny commit",
                "A denial reason is required.");
            await reason.ShowDialog(this);
            if (reason.Result is not null)
            {
                await store.DecideCommitAsync(
                    GoalCommitDecision.Deny,
                    new(reason.Result),
                    cancellationToken);
            }
        };
    }

    private async Task ConfirmAndCommitAsync(bool resuming)
    {
        GoalCommitApprovalView? approval = store.Current.Goals.CommitApproval;
        if (approval is null)
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

    private void Render(GoalManagementState state)
    {
        GoalCommitPreview? preview = state.CommitPreview;
        GoalCommitApprovalView? approval = state.CommitApproval;
        fingerprint.Text = GoalPresentationFormatter.FormatCommitFingerprint(preview, approval);
        diff.Text = approval?.Diff.Value ?? preview?.Diff.Value ?? "No exact diff is available.";
        bool busy = state.IsBusy;
        requestFields.IsVisible = preview is not null && approval is null;
        request.IsEnabled = !busy && preview is not null && approval is null;
        approve.IsVisible = approval?.State is GoalCommitApprovalState.Pending;
        approve.IsEnabled = !busy && approval?.State is GoalCommitApprovalState.Pending;
        deny.IsVisible = approval?.State is GoalCommitApprovalState.Pending;
        deny.IsEnabled = !busy && approval?.State is GoalCommitApprovalState.Pending;
        resume.IsVisible = approval?.State is GoalCommitApprovalState.Approved;
        resume.IsEnabled = !busy && approval?.State is GoalCommitApprovalState.Approved;
        status.Text = busy ? "Revalidating exact commit state…" : state.Status ?? string.Empty;
    }

    private static TextBox Viewer() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
    };
}

internal sealed class ExactCommitConfirmationDialog : Window
{
    internal ExactCommitConfirmationDialog(GoalCommitApprovalView approval, bool resuming)
    {
        Title = resuming ? "Resume approved commit" : "Approve exact commit";
        Width = 720;
        Height = 460;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button commit = new()
        {
            Content = resuming ? "Revalidate and resume commit" : "Approve and commit",
        };
        commit.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = resuming
                        ? "Resume the already-approved exact commit?"
                        : "Approve this exact fingerprint and create the local commit?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Branch: {approval.Branch.Value}\n" +
                           $"Expected HEAD: {approval.ExpectedHead.Value}\n" +
                           $"Complete diff SHA-256: {approval.DiffHash.Value}\n" +
                           $"Changed files: {approval.ChangedFileCount.Value}\n" +
                           $"Author: {approval.AuthorName.Value} <{approval.AuthorEmail.Value}>\n\n" +
                           approval.CommitMessage.Value,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Harness.NET revalidates the branch, HEAD, and complete diff immediately " +
                           "before committing. It does not merge, rebase, cherry-pick, push, or use " +
                           "the network.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, commit },
                },
            },
        };
    }
}

internal sealed class SemanticContextDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly GoalView goal;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly TextBox profile = Viewer();
    private readonly TextBox rebuildResult = Viewer();
    private readonly TextBox searchResult = Viewer();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button rebuild = new() { Content = "Rebuild index…" };
    private readonly Button search = new() { Content = "Preview search…" };
    private readonly Button cancel = new() { Content = "Cancel operation" };

    internal SemanticContextDialog(
        AvaloniaPresentationStore store,
        GoalView goal,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.goal = goal;
        this.cancellationToken = cancellationToken;
        Title = "Semantic context";
        Width = 940;
        Height = 700;
        MinWidth = 720;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Goals)));
        Closed += (_, _) =>
        {
            if (store.Current.Goals.IsSemanticRunning)
            {
                store.CancelSemanticOperation();
            }

            subscription.Dispose();
        };
    }

    private Control BuildContent()
    {
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        TabControl tabs = new()
        {
            ItemsSource = new TabItem[]
            {
                new() { Header = "Status & route", Content = profile },
                new() { Header = "Last rebuild", Content = rebuildResult },
                new() { Header = "Search matches", Content = searchResult },
            },
        };
        Grid root = new()
        {
            RowDefinitions = new("Auto,*,Auto,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(20),
        };
        root.Children.Add(new TextBlock
        {
            Text = "Goal-attributed semantic context",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { rebuild, search, cancel },
        };
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        Grid footer = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(status);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return root;
    }

    private void WireInteractions()
    {
        rebuild.Click += async (_, _) =>
        {
            SemanticIndexStatusResult? semanticStatus = store.Current.Goals.SemanticStatus;
            if (semanticStatus is null)
            {
                return;
            }

            SemanticRebuildConfirmationDialog confirmation = new(
                goal,
                semanticStatus,
                store.Current.Goals.Cost);
            if (await confirmation.ShowDialog<bool>(this))
            {
                await store.RebuildSemanticIndexAsync(goal.Id, cancellationToken);
            }
        };
        search.Click += async (_, _) =>
        {
            TextEntryDialog query = new(
                "Preview semantic context",
                "Query (one attributed embedding call, maximum 2,000 characters)",
                "Search up to 8 matches",
                "A query is required.");
            await query.ShowDialog(this);
            if (query.Result is not null)
            {
                await store.SearchSemanticContextAsync(goal.Id, query.Result, cancellationToken);
            }
        };
        cancel.Click += (_, _) => store.CancelSemanticOperation();
    }

    private void Render(GoalManagementState state)
    {
        profile.Text = GoalPresentationFormatter.FormatSemanticStatus(
            state.SemanticStatus,
            goal,
            state.Cost);
        rebuildResult.Text = GoalPresentationFormatter.FormatSemanticRebuild(state.SemanticRebuild);
        searchResult.Text = GoalPresentationFormatter.FormatSemanticSearch(state.SemanticSearch);
        bool busy = state.IsSemanticRunning;
        SemanticIndexStatusResult? semanticStatus = state.SemanticStatus;
        rebuild.IsEnabled = !state.IsBusy && semanticStatus is { Error: null };
        search.IsEnabled = !state.IsBusy &&
                           semanticStatus is { Error: null, CurrentPartition: not null };
        cancel.IsVisible = busy;
        cancel.IsEnabled = busy;
        status.Text = busy ? "Embedding operation running…" : state.Status ?? string.Empty;
    }

    private static TextBox Viewer() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
    };
}

internal sealed class SemanticRebuildConfirmationDialog : Window
{
    internal SemanticRebuildConfirmationDialog(
        GoalView goal,
        SemanticIndexStatusResult status,
        RemoteCostReport? cost)
    {
        Title = "Confirm semantic rebuild";
        Width = 680;
        Height = 390;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button rebuild = new() { Content = "Rebuild semantic index" };
        rebuild.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = $"Rebuild with {status.Profile.Access} " +
                           $"{status.Profile.Provider.Value}/{status.Profile.Model.Value}?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Dimensions: {status.Profile.Dimensions.Value}; chunking version: " +
                           $"{status.Profile.ChunkingVersion.Value}. The final input size depends " +
                           "on eligible Git-tracked text.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = GoalPresentationFormatter.FormatCostSummary(goal, cost),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Remote batches use strict no-collection and zero-data-retention routing, " +
                           "remain goal-attributed, and fail closed at the cap. Cancellation preserves " +
                           "the previous ready partition.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, rebuild },
                },
            },
        };
    }
}

internal static class GoalPresentationFormatter
{
    internal static string FormatSelections(IReadOnlyList<GoalModelSelectionView> selections) =>
        selections.Count == 0
            ? "ROLE MODELS\nUnavailable"
            : "ROLE MODELS\n" + string.Join('\n', selections.Select(selection =>
                $"{selection.Role,-11} {selection.Provider.Value}/{selection.Model.Value} | " +
                $"{selection.Access} | {(selection.IsExplicit ? "goal-selected" : "configured default")}"));

    internal static string FormatCandidate(GoalModelCandidate candidate) =>
        $"{candidate.Access,-6} | {candidate.Provider.Value}/{candidate.Model.Value}" +
        (candidate.InputPrice is null || candidate.OutputPrice is null
            ? " | pricing unavailable"
            : $" | in ${candidate.InputPrice.Value:0.######}/M" +
              $" out ${candidate.OutputPrice.Value:0.######}/M" +
              (candidate.RequestPrice?.Value > 0
                  ? $" req ${candidate.RequestPrice.Value:0.######}"
                  : string.Empty));

    internal static string FormatRoutesAndCost(
        GoalView? goal,
        IReadOnlyList<GoalModelSelectionView> selections,
        RemoteCostReport? cost)
    {
        if (goal is null)
        {
            return "Select a goal.";
        }

        string costText;
        if (goal.RemoteBudget is null)
        {
            costText = "REMOTE COST\nNot authorized; no remote-model spend is permitted.";
        }
        else if (cost is null)
        {
            costText = $"REMOTE COST\nCap: ${ToUsd(goal.RemoteBudget.Value)}\n" +
                       "No reservations or charges recorded.";
        }
        else
        {
            costText = string.Join(
                '\n',
                "REMOTE COST",
                $"Cap:        ${ToUsd(cost.CostCap.Value)}",
                $"Reserved:   ${ToUsd(cost.ReservedCost.Value)}",
                $"Reconciled: ${ToUsd(cost.ReconciledCost.Value)}",
                $"Remaining:  ${ToUsd(cost.RemainingCost.Value)}",
                $"Overage:    ${ToUsd(cost.Overage.Value)}");
            if (cost.Items.Count > 0)
            {
                costText += "\n\nATTRIBUTION\n" + string.Join('\n', cost.Items.Select(item =>
                    $"{item.State} | {item.Kind} | {item.Provider}/{item.Model} | " +
                    $"estimated ${ToUsd(item.EstimatedCost.Value)} | " +
                    (item.ActualCost is null
                        ? "actual pending"
                        : $"actual ${ToUsd(item.ActualCost.Value)}")));
            }
        }

        return FormatSelections(selections) + "\n\n" + costText;
    }

    internal static string FormatWorkflow(GoalWorkflowSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "No production run has been started.";
        }

        return string.Join(
            "\n",
            $"Run: {snapshot.Id.Value}",
            $"State: {snapshot.State}",
            $"Completed review cycles: {snapshot.ReviewCycle.Value}",
            snapshot.RequiresUserDirection ? "USER DIRECTION REQUIRED" : string.Empty,
            string.Empty,
            "DELEGATED TASKS",
            snapshot.Tasks.Count == 0
                ? "No delegated tasks yet."
                : string.Join("\n\n", snapshot.Tasks.Select(task =>
                    $"{task.Sequence.Value}. [{task.State}] {task.Title.Value}\n" +
                    $"Objective: {task.Objective.Value}\n" +
                    $"File areas:\n{task.FileAreas.Value}\n" +
                    $"Acceptance criteria:\n{task.AcceptanceCriteria.Value}" +
                    (task.Report is null ? string.Empty : $"\nReport:\n{task.Report.Value}"))),
            string.Empty,
            "ACTIVITY",
            snapshot.Activities.Count == 0
                ? "No activity yet."
                : string.Join("\n", snapshot.Activities.Select(item =>
                    $"{item.Sequence}. {item.Actor} | {item.Kind} | {item.Summary.Value}")),
            string.Empty,
            "EVIDENCE",
            snapshot.Evidence.Count == 0
                ? "No evidence yet."
                : string.Join("\n\n", snapshot.Evidence.Select(item =>
                    $"[{item.Sequence}] {item.Title.Value}\n{item.Content.Value}")));
    }

    internal static string StartDisclosure(GoalManagementState state) => string.Join(
        '\n',
        "This starts one bounded Lead call. It authorizes no repository mutation.",
        FormatSelections(state.ModelSelections),
        FormatCostSummary(state));

    internal static string ResumeDisclosure(GoalView goal, GoalManagementState state)
    {
        GoalWorkflowSnapshot? workflow = state.Workflow;
        int pendingTasks = workflow?.Tasks.Count(task => task.State is GoalTaskState.Pending) ?? 0;
        int remainingReviews = Math.Max(
            0,
            goal.ReviewCycleLimit.Value - (workflow?.ReviewCycle.Value ?? 0));
        int maximumCorrections = Math.Max(0, remainingReviews - 1);
        return string.Join(
            '\n',
            $"Maximum remaining role calls: {pendingTasks} delegated Implementer + " +
            $"{remainingReviews} Reviewer + {maximumCorrections} correction Implementer.",
            "Acceptance may stop earlier. Model-directed semantic searches may add separately " +
            "attributed embedding calls; the aggregate goal cap always applies.",
            FormatSelections(state.ModelSelections),
            FormatCostSummary(state));
    }

    internal static string ToUsd(long microUsd) =>
        (microUsd / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);

    internal static string FormatSemanticStatus(
        SemanticIndexStatusResult? status,
        GoalView goal,
        RemoteCostReport? cost)
    {
        if (status is null)
        {
            return "Semantic status has not been inspected.";
        }

        string partition = status.CurrentPartition is null
            ? "No compatible index is ready."
            : $"Ready partition: {status.CurrentPartition.FileCount} files, " +
              $"{status.CurrentPartition.ChunkCount} chunks, completed " +
              $"{status.CurrentPartition.CompletedAt:O}.";
        return string.Join(
            '\n',
            status.Error is null ? "Status inspection performed without inference." : $"Error: {status.Error}",
            $"Embedding route: {status.Profile.Access} {status.Profile.Provider.Value}/" +
            $"{status.Profile.Model.Value}",
            $"Dimensions: {status.Profile.Dimensions.Value}",
            $"Chunking version: {status.Profile.ChunkingVersion.Value}",
            partition,
            string.Empty,
            FormatCostSummary(goal, cost));
    }

    internal static string FormatSemanticRebuild(SemanticIndexResult? result) => result is null
        ? "No rebuild has been run in this session."
        : string.Join(
            '\n',
            result.Error is null ? "State: ready" : $"Error: {result.Error}",
            $"Tracked files: {result.TrackedFileCount}",
            $"Skipped files: {result.SkippedFileCount}",
            $"Truncated: {result.IsTruncated}",
            $"Indexed files: {result.Partition?.FileCount ?? 0}",
            $"Chunks: {result.Partition?.ChunkCount ?? 0}",
            $"Embedding input tokens: {result.Usage.InputTokens}",
            $"Embedding cost: {FormatEmbeddingCost(result.Usage)}");

    internal static string FormatSemanticSearch(SemanticSearchResult? result) => result is null
        ? "No semantic preview has been run in this session."
        : string.Join(
            '\n',
            result.Error is null ? $"Matches: {result.Matches.Count}" : $"Error: {result.Error}",
            $"Embedding input tokens: {result.Usage.InputTokens}",
            $"Embedding cost: {FormatEmbeddingCost(result.Usage)}",
            string.Empty,
            result.Matches.Count == 0
                ? "No context matches."
                : string.Join("\n\n", result.Matches.Select((match, index) =>
                    $"{index + 1}. {match.Path}:{match.StartLine}-{match.EndLine} " +
                    $"| distance {match.Distance.Value:F6}\n{match.Content}")));

    internal static string FormatCommitFingerprint(
        GoalCommitPreview? preview,
        GoalCommitApprovalView? approval)
    {
        if (approval is not null)
        {
            return string.Join(
                '\n',
                $"State: {approval.State}",
                $"Branch: {approval.Branch.Value}",
                $"Expected HEAD: {approval.ExpectedHead.Value}",
                $"Complete diff SHA-256: {approval.DiffHash.Value}",
                $"Changed files: {approval.ChangedFileCount.Value}",
                $"Author: {approval.AuthorName.Value} <{approval.AuthorEmail.Value}>",
                $"Requested: {approval.RequestedAt:O}",
                approval.DecisionReason is null
                    ? string.Empty
                    : $"Decision reason: {approval.DecisionReason.Value}",
                approval.CommitSha is null ? string.Empty : $"Commit: {approval.CommitSha.Value}");
        }

        return preview is null
            ? "No commit preview or approval is available."
            : string.Join(
                '\n',
                "State: unrequested exact preview",
                $"Branch: {preview.Branch.Value}",
                $"HEAD: {preview.Head.Value}",
                $"Complete diff SHA-256: {preview.DiffHash.Value}",
                $"Changed files: {preview.ChangedFileCount.Value}");
    }

    private static string FormatCostSummary(GoalManagementState state)
    {
        GoalView? goal = state.SelectedGoal;
        if (goal?.RemoteBudget is null)
        {
            return "Remote spend: not authorized (local-only goal).";
        }

        RemoteCostReport? cost = state.Cost;
        return cost is null
            ? $"Remote cap ${ToUsd(goal.RemoteBudget.Value)} | no spend recorded"
            : $"Remote cap ${ToUsd(cost.CostCap.Value)} | " +
              $"reserved ${ToUsd(cost.ReservedCost.Value)} | " +
              $"spent ${ToUsd(cost.ReconciledCost.Value)} | " +
              $"remaining ${ToUsd(cost.RemainingCost.Value)}";
    }

    internal static string FormatCostSummary(GoalView goal, RemoteCostReport? cost) =>
        goal.RemoteBudget is null
            ? "Remote spend: not authorized (local-only goal)."
            : cost is null
                ? $"Remote cap ${ToUsd(goal.RemoteBudget.Value)} | no spend recorded"
                : $"Remote cap ${ToUsd(cost.CostCap.Value)} | " +
                  $"reserved ${ToUsd(cost.ReservedCost.Value)} | " +
                  $"spent ${ToUsd(cost.ReconciledCost.Value)} | " +
                  $"remaining ${ToUsd(cost.RemainingCost.Value)}";

    private static string FormatEmbeddingCost(EmbeddingUsageView usage) => usage.Cost is null
        ? "$0.000000"
        : $"${usage.Cost.Value / 1_000_000m:F6}";
}
