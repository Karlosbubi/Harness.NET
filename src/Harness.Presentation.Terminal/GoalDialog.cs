using System.Collections.ObjectModel;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Terminal.Gui.App;
using Terminal.Gui.Editor;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Harness.Presentation.Terminal;

internal sealed class GoalDialog : Dialog
{
    private readonly IApplication application;
    private readonly IGoalService goalService;
    private readonly IRemoteCostService remoteCostService;
    private readonly string workspaceId;
    private readonly CancellationToken cancellationToken;
    private readonly ListView goalList;
    private readonly Button createGoal;
    private readonly Button inspectGoal;
    private readonly Button proposePlan;
    private readonly Button approvePlan;
    private readonly Button denyPlan;
    private readonly Label status;
    private IReadOnlyList<GoalView> goals;

    internal GoalDialog(
        IApplication application,
        IGoalService goalService,
        IRemoteCostService remoteCostService,
        string workspaceId,
        IReadOnlyList<GoalView> goals,
        CancellationToken cancellationToken)
    {
        this.application = application;
        this.goalService = goalService;
        this.remoteCostService = remoteCostService;
        this.workspaceId = workspaceId;
        this.goals = goals;
        this.cancellationToken = cancellationToken;

        Title = "Goals and plans";
        Width = Dim.Percent(90);
        Height = Dim.Percent(85);

        goalList = new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(7),
        };
        goalList.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await InspectAsync();
        };

        createGoal = CommandButton("_New goal", 0, () => CreateAsync());
        inspectGoal = CommandButton("_Inspect", Pos.Right(createGoal) + 1, () => InspectAsync());
        proposePlan = CommandButton("_Propose plan", Pos.Right(inspectGoal) + 1, () => ProposeAsync());
        approvePlan = CommandButton("_Approve", Pos.Right(proposePlan) + 1, () => DecideAsync(approve: true));
        denyPlan = CommandButton("_Deny", Pos.Right(approvePlan) + 1, () => DecideAsync(approve: false));
        goalList.ValueChanged += (_, _) => SetCommandsEnabled(enabled: true);
        SetGoalSource();
        status = new()
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
            Height = 2,
            Text = goals.Count == 0 ? "Create the first goal." : $"{goals.Count} goal(s)",
        };
        Add(goalList, createGoal, inspectGoal, proposePlan, approvePlan, denyPlan, status);
        AddButton(new Button { Title = "_Close" });
    }

    internal IReadOnlyList<GoalView> Goals => goals;

    private Button CommandButton(string title, Pos x, Func<Task> command)
    {
        Button button = new()
        {
            Title = title,
            X = x,
            Y = Pos.AnchorEnd(6),
        };
        button.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await RunCommandAsync(command);
        };
        return button;
    }

    private async Task CreateAsync()
    {
        GoalCreateRequest? request = await CollectGoalAsync();
        if (request is null)
        {
            return;
        }

        GoalResult result = await goalService.CreateAsync(request, cancellationToken);
        await ReloadAsync();
        status.Text = result.Error ?? $"Created '{result.Goal?.Title}'.";
    }

    private async Task InspectAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        PlanView? plan = await goalService.GetCurrentPlanAsync(goal.Id, cancellationToken);
        RemoteCostReport? cost = await remoteCostService.GetAsync(goal.Id, cancellationToken);
        using Dialog dialog = ReadOnlyDialog(
            $"Goal | {goal.State}",
            GoalTextFormatter.FormatDetails(goal, plan, cost));
        await application.RunAsync(dialog, cancellationToken);
    }

    private async Task ProposeAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        string? content = await CollectMultilineAsync("Propose plan", "Plan", requireValue: true);
        if (content is null)
        {
            return;
        }

        PlanResult result = await goalService.ProposePlanAsync(
            new(goal.Id, content),
            cancellationToken);
        await ReloadAsync();
        status.Text = result.Error ?? $"Plan revision {result.Plan?.Revision.Value} awaits approval.";
    }

    private async Task DecideAsync(bool approve)
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        PlanView? plan = await goalService.GetCurrentPlanAsync(goal.Id, cancellationToken);
        if (plan is null)
        {
            status.Text = "The selected goal has no plan.";
            return;
        }

        string? reason = null;
        if (approve)
        {
            int? choice = MessageBox.Query(
                application,
                "Approve plan",
                "Approve this plan and create its isolated goal worktree?",
                "_Approve",
                "_Cancel");
            if (choice != 0)
            {
                return;
            }
        }
        else
        {
            reason = await CollectMultilineAsync("Deny plan", "Required reason", requireValue: true);
            if (reason is null)
            {
                return;
            }
        }

        PlanResult result = await goalService.DecidePlanAsync(new(
            goal.Id,
            plan.Id,
            approve ? PlanDecision.Approve : PlanDecision.Deny,
            reason), cancellationToken);
        await ReloadAsync();
        status.Text = result.Error ?? (approve
            ? $"Approved | {result.Worktree?.Branch}"
            : "Denied | plan revision required");
    }

    private async Task<GoalCreateRequest?> CollectGoalAsync()
    {
        using Dialog dialog = new()
        {
            Title = "New goal",
            Width = Dim.Percent(80),
            Height = 20,
        };
        TextField title = Field(dialog, "Title", 0, string.Empty);
        Editor objective = new()
        {
            X = 0,
            Y = 4,
            Width = Dim.Fill(),
            Height = 5,
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar,
        };
        dialog.Add(new Label { Text = "Objective", X = 0, Y = 3 }, objective);
        TextField reviewLimit = Field(dialog, "Review-cycle limit (1-20)", 10, "3");
        TextField remoteBudget = Field(
            dialog,
            "Remote budget USD (blank = local only)",
            12,
            string.Empty);
        Label validation = new() { X = 0, Y = 14, Width = Dim.Fill(), Height = 1 };
        GoalCreateRequest? result = null;
        Button create = new() { Title = "_Create" };
        create.Accepting += (_, args) =>
        {
            args.Handled = true;
            if (!int.TryParse(reviewLimit.Text?.ToString(), out int reviewCycles))
            {
                validation.Text = "Review-cycle limit must be an integer.";
                return;
            }

            string budgetText = remoteBudget.Text?.ToString()?.Trim() ?? string.Empty;
            MicroUsdAmount? budget = null;
            if (budgetText.Length > 0)
            {
                if (!GoalTextFormatter.TryParseUsd(budgetText, out long parsedBudget))
                {
                    validation.Text = "Remote budget must be a positive USD amount.";
                    return;
                }

                budget = new(parsedBudget);
            }

            result = new(
                workspaceId,
                title.Text?.ToString() ?? string.Empty,
                objective.Text?.ToString() ?? string.Empty,
                new(reviewCycles),
                budget);
            dialog.RequestStop();
        };
        dialog.Add(validation);
        dialog.AddButton(create);
        dialog.AddButton(new Button { Title = "_Cancel" });
        await application.RunAsync(dialog, cancellationToken);
        return result;
    }

    private async Task<string?> CollectMultilineAsync(
        string title,
        string label,
        bool requireValue)
    {
        using Dialog dialog = new()
        {
            Title = title,
            Width = Dim.Percent(80),
            Height = Dim.Percent(70),
        };
        dialog.Add(new Label { Text = label, X = 0, Y = 0 });
        Editor editor = new()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(3),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar,
        };
        Label validation = new() { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill() };
        string? result = null;
        Button save = new() { Title = "_Save" };
        save.Accepting += (_, args) =>
        {
            args.Handled = true;
            string value = editor.Text?.ToString() ?? string.Empty;
            if (requireValue && string.IsNullOrWhiteSpace(value))
            {
                validation.Text = "A value is required.";
                return;
            }

            result = value;
            dialog.RequestStop();
        };
        dialog.Add(editor, validation);
        dialog.AddButton(save);
        dialog.AddButton(new Button { Title = "_Cancel" });
        await application.RunAsync(dialog, cancellationToken);
        return result;
    }

    private async Task ReloadAsync()
    {
        goals = await goalService.ListAsync(workspaceId, cancellationToken);
        application.Invoke(SetGoalSource);
    }

    private void SetGoalSource()
    {
        goalList.SetSource(new ObservableCollection<string>(goals
            .Select(GoalTextFormatter.FormatListItem)
            .ToArray()));
        SetCommandsEnabled(enabled: true);
    }

    private GoalView? SelectedGoal()
    {
        int selected = goalList.SelectedItem ?? -1;
        return selected >= 0 && selected < goals.Count ? goals[selected] : null;
    }

    private async Task RunCommandAsync(Func<Task> command)
    {
        try
        {
            SetCommandsEnabled(false);
            await command();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RequestStop();
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = exception.Message);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                application.Invoke(() => SetCommandsEnabled(true));
            }
        }
    }

    private void SetCommandsEnabled(bool enabled)
    {
        GoalState? state = SelectedGoal()?.State;
        createGoal.Enabled = enabled;
        inspectGoal.Enabled = enabled && state is not null;
        proposePlan.Enabled = enabled && state is GoalState.Draft or GoalState.NeedsPlanRevision;
        approvePlan.Enabled = enabled && state is GoalState.AwaitingPlanApproval;
        denyPlan.Enabled = enabled && state is GoalState.AwaitingPlanApproval;
    }

    private static TextField Field(Dialog dialog, string label, int y, string value)
    {
        dialog.Add(new Label { Text = label, X = 0, Y = y });
        TextField field = new()
        {
            Text = value,
            X = 0,
            Y = y + 1,
            Width = Dim.Fill(),
        };
        dialog.Add(field);
        return field;
    }

    private static Dialog ReadOnlyDialog(string title, string text)
    {
        Dialog dialog = new()
        {
            Title = title,
            Width = Dim.Percent(85),
            Height = Dim.Percent(80),
        };
        dialog.Add(new Editor
        {
            Text = text,
            ReadOnly = true,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar |
                               ViewportSettingsFlags.HasHorizontalScrollBar,
        });
        dialog.AddButton(new Button { Title = "_Close" });
        return dialog;
    }
}
