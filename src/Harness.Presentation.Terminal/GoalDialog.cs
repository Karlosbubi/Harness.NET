using System.Collections.ObjectModel;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;
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
    private readonly IGoalModelService modelService;
    private readonly IGoalWorkflowService workflowService;
    private readonly IGoalAcceptanceService acceptanceService;
    private readonly string workspaceId;
    private readonly CancellationToken cancellationToken;
    private readonly ListView goalList;
    private readonly Button createGoal;
    private readonly Button inspectGoal;
    private readonly Button manageModels;
    private readonly Button proposePlan;
    private readonly Button approvePlan;
    private readonly Button denyPlan;
    private readonly Button startRun;
    private readonly Button resumeRun;
    private readonly Button inspectRun;
    private readonly Button manageCommit;
    private readonly Label status;
    private IReadOnlyList<GoalView> goals;

    internal GoalDialog(
        IApplication application,
        IGoalService goalService,
        IRemoteCostService remoteCostService,
        IGoalModelService modelService,
        IGoalWorkflowService workflowService,
        IGoalAcceptanceService acceptanceService,
        string workspaceId,
        IReadOnlyList<GoalView> goals,
        CancellationToken cancellationToken)
    {
        this.application = application;
        this.goalService = goalService;
        this.remoteCostService = remoteCostService;
        this.modelService = modelService;
        this.workflowService = workflowService;
        this.acceptanceService = acceptanceService;
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
            Height = Dim.Fill(11),
        };
        goalList.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await InspectAsync();
        };

        createGoal = CommandButton("_New goal", 0, Pos.AnchorEnd(8), () => CreateAsync());
        inspectGoal = CommandButton(
            "_Inspect",
            Pos.Right(createGoal) + 1,
            Pos.AnchorEnd(8),
            () => InspectAsync());
        manageModels = CommandButton(
            "_Models",
            Pos.Right(inspectGoal) + 1,
            Pos.AnchorEnd(8),
            () => ManageModelsAsync());
        proposePlan = CommandButton("_Propose plan", 0, Pos.AnchorEnd(6), () => ProposeAsync());
        approvePlan = CommandButton(
            "_Approve",
            Pos.Right(proposePlan) + 1,
            Pos.AnchorEnd(6),
            () => DecideAsync(approve: true));
        denyPlan = CommandButton(
            "_Deny",
            Pos.Right(approvePlan) + 1,
            Pos.AnchorEnd(6),
            () => DecideAsync(approve: false));
        startRun = CommandButton("Start _run", 0, Pos.AnchorEnd(4), () => StartRunAsync());
        resumeRun = CommandButton(
            "_Continue run",
            Pos.Right(startRun) + 1,
            Pos.AnchorEnd(4),
            () => ResumeRunAsync());
        inspectRun = CommandButton(
            "Run _evidence",
            Pos.Right(resumeRun) + 1,
            Pos.AnchorEnd(4),
            () => InspectRunAsync());
        manageCommit = CommandButton(
            "_Commit",
            Pos.Right(inspectRun) + 1,
            Pos.AnchorEnd(4),
            () => ManageCommitAsync());
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
        Add(
            goalList,
            createGoal,
            inspectGoal,
            manageModels,
            proposePlan,
            approvePlan,
            denyPlan,
            startRun,
            resumeRun,
            inspectRun,
            manageCommit,
            status);
        AddButton(new Button { Title = "_Close" });
    }

    internal IReadOnlyList<GoalView> Goals => goals;

    private Button CommandButton(string title, Pos x, Pos y, Func<Task> command)
    {
        Button button = new()
        {
            Title = title,
            X = x,
            Y = y,
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
        IReadOnlyList<GoalModelSelectionView> selections =
            await modelService.GetSelectionsAsync(goal.Id, cancellationToken);
        using Dialog dialog = ReadOnlyDialog(
            $"Goal | {goal.State}",
            GoalTextFormatter.FormatDetails(goal, plan, cost, selections));
        await application.RunAsync(dialog, cancellationToken);
    }

    private async Task ManageModelsAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        status.Text = "Discovering provider catalogs; no inference is performed.";
        GoalModelCatalog catalog = await modelService.DiscoverAsync(goal.Id, cancellationToken);
        IReadOnlyList<GoalModelSelectionView> selections =
            await modelService.GetSelectionsAsync(goal.Id, cancellationToken);
        using GoalModelDialog dialog = new(
            application,
            modelService,
            goal,
            catalog,
            selections,
            cancellationToken);
        await application.RunAsync(dialog, cancellationToken);
        status.Text = "Role model selection updated.";
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

    private async Task StartRunAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        MaximumAgentOutputTokens[]? maxima = await CollectOutputMaximaAsync(
            "Start lead planning",
            goal,
            [AgentRole.Lead],
            ["Lead maximum output tokens"]);
        if (maxima is null)
        {
            return;
        }

        GoalWorkflowSnapshot? latest = null;
        await foreach (GoalWorkflowSnapshot snapshot in workflowService.StartPlanningAsync(
                           new(goal.Id, maxima[0]), cancellationToken))
        {
            latest = snapshot;
            status.Text = $"Run {snapshot.State} | {snapshot.Activities[^1].Kind}";
        }

        await ReloadAsync();
        if (latest is not null)
        {
            await ShowRunAsync(latest);
        }
    }

    private async Task ResumeRunAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        MaximumAgentOutputTokens[]? maxima = await CollectOutputMaximaAsync(
            "Continue production run",
            goal,
            [AgentRole.Implementer, AgentRole.Reviewer],
            ["Implementer maximum output tokens", "Reviewer maximum output tokens"]);
        if (maxima is null)
        {
            return;
        }

        GoalWorkflowSnapshot? latest = null;
        await foreach (GoalWorkflowSnapshot snapshot in workflowService.ResumeAsync(
                           new(goal.Id, maxima[0], maxima[1]), cancellationToken))
        {
            latest = snapshot;
            status.Text = $"Run {snapshot.State} | {snapshot.Activities[^1].Kind}";
        }

        if (latest is not null)
        {
            await ShowRunAsync(latest);
        }
    }

    private async Task InspectRunAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        GoalWorkflowSnapshot? snapshot = await workflowService.GetLatestAsync(
            goal.Id, cancellationToken);
        if (snapshot is null)
        {
            status.Text = "The selected goal has no production run.";
            return;
        }

        await ShowRunAsync(snapshot);
    }

    private async Task ManageCommitAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        GoalWorkflowSnapshot? workflow = await workflowService.GetLatestAsync(
            goal.Id, cancellationToken);
        if (workflow is null)
        {
            status.Text = "The selected goal has no production run.";
            return;
        }

        GoalCommitApprovalView? approval = await acceptanceService.GetAsync(
            goal.Id, workflow.Id, cancellationToken);
        if (approval is null)
        {
            GoalCommitPreviewResult previewResult = await acceptanceService.PreviewAsync(
                goal.Id, cancellationToken);
            if (previewResult.Preview is null)
            {
                status.Text = previewResult.Error ?? "The goal is not ready for commit approval.";
                return;
            }

            GoalCommitApprovalRequest? request = await CollectCommitRequestAsync(
                previewResult.Preview);
            if (request is null)
            {
                return;
            }

            GoalCommitApprovalResult requested = await acceptanceService.RequestAsync(
                request, cancellationToken);
            status.Text = requested.Error ??
                "Exact commit request recorded as Pending; open Commit again to approve or deny.";
            return;
        }

        if (approval.State is GoalCommitApprovalState.Committed or
            GoalCommitApprovalState.Denied)
        {
            using Dialog detail = ReadOnlyDialog(
                $"Commit | {approval.State}", GoalCommitTextFormatter.Format(approval));
            await application.RunAsync(detail, cancellationToken);
            return;
        }

        GoalCommitDecision? decision;
        if (approval.State is GoalCommitApprovalState.Pending)
        {
            decision = await CollectCommitDecisionAsync(approval);
        }
        else
        {
            int? choice = MessageBox.Query(
                application,
                "Resume approved commit",
                $"Revalidate and commit the approved fingerprint?\n" +
                $"Branch: {approval.Branch.Value}\nDiff SHA-256: {approval.DiffHash.Value}",
                "_Resume commit",
                "_Cancel");
            decision = choice == 0 ? GoalCommitDecision.Approve : null;
        }

        if (decision is null)
        {
            return;
        }

        if (decision is GoalCommitDecision.Deny)
        {
            string? reason = await CollectMultilineAsync(
                "Deny exact commit", "Required reason", requireValue: true);
            if (reason is null)
            {
                return;
            }

            GoalCommitApprovalResult denied = await acceptanceService.DecideAsync(new(
                approval.Id, GoalCommitDecision.Deny, new(reason)), cancellationToken);
            status.Text = denied.Error ?? "Commit denied; no Git commit was created.";
            return;
        }

        GoalCommitApprovalResult committed = await acceptanceService.DecideAsync(new(
            approval.Id, GoalCommitDecision.Approve, Reason: null), cancellationToken);
        status.Text = committed.Error ??
            $"Committed exact approved diff | {committed.Approval?.CommitSha?.Value}";
    }

    private async Task<GoalCommitDecision?> CollectCommitDecisionAsync(
        GoalCommitApprovalView approval)
    {
        using Dialog dialog = ReadOnlyDialog(
            "Decide exact commit request", GoalCommitTextFormatter.Format(approval));
        GoalCommitDecision? result = null;
        Button approve = new() { Title = "_Approve exact diff" };
        approve.Accepting += (_, args) =>
        {
            args.Handled = true;
            result = GoalCommitDecision.Approve;
            dialog.RequestStop();
        };
        Button deny = new() { Title = "_Deny" };
        deny.Accepting += (_, args) =>
        {
            args.Handled = true;
            result = GoalCommitDecision.Deny;
            dialog.RequestStop();
        };
        dialog.AddButton(approve);
        dialog.AddButton(deny);
        await application.RunAsync(dialog, cancellationToken);
        return result;
    }

    private async Task<GoalCommitApprovalRequest?> CollectCommitRequestAsync(
        GoalCommitPreview preview)
    {
        using Dialog dialog = new()
        {
            Title = "Request exact commit approval",
            Width = Dim.Percent(90),
            Height = Dim.Percent(90),
        };
        dialog.Add(new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = $"Branch: {preview.Branch.Value} | files: {preview.ChangedFileCount.Value}\n" +
                   $"HEAD: {preview.Head.Value}\nDiff SHA-256: {preview.DiffHash.Value}",
        });
        Editor diff = new()
        {
            Text = preview.Diff.Value,
            ReadOnly = true,
            X = 0,
            Y = 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(10),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar |
                               ViewportSettingsFlags.HasHorizontalScrollBar,
        };
        Pos fieldsY = Pos.AnchorEnd(9);
        TextField message = Field(dialog, "Commit message", fieldsY, string.Empty);
        TextField authorName = Field(dialog, "Author name", Pos.AnchorEnd(7), string.Empty);
        TextField authorEmail = Field(dialog, "Author email", Pos.AnchorEnd(5), string.Empty);
        Label validation = new()
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
        };
        GoalCommitApprovalRequest? result = null;
        Button request = new() { Title = "_Record pending request" };
        request.Accepting += (_, args) =>
        {
            args.Handled = true;
            string messageValue = message.Text?.ToString() ?? string.Empty;
            string nameValue = authorName.Text?.ToString() ?? string.Empty;
            string emailValue = authorEmail.Text?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(messageValue) ||
                string.IsNullOrWhiteSpace(nameValue) ||
                string.IsNullOrWhiteSpace(emailValue))
            {
                validation.Text = "Commit message, author name, and author email are required.";
                return;
            }

            result = new(
                preview.GoalId,
                preview.RunId,
                preview.Head,
                preview.DiffHash,
                new(messageValue),
                new(nameValue),
                new(emailValue));
            dialog.RequestStop();
        };
        dialog.Add(diff, validation);
        dialog.AddButton(request);
        dialog.AddButton(new Button { Title = "_Cancel" });
        await application.RunAsync(dialog, cancellationToken);
        return result;
    }

    private async Task ShowRunAsync(GoalWorkflowSnapshot snapshot)
    {
        using Dialog dialog = ReadOnlyDialog(
            $"Goal run | {snapshot.State}",
            GoalWorkflowTextFormatter.Format(snapshot));
        await application.RunAsync(dialog, cancellationToken);
    }

    private async Task<MaximumAgentOutputTokens[]?> CollectOutputMaximaAsync(
        string title,
        GoalView goal,
        IReadOnlyList<AgentRole> roles,
        IReadOnlyList<string> labels)
    {
        RemoteCostReport? cost = await remoteCostService.GetAsync(goal.Id, cancellationToken);
        IReadOnlyList<GoalModelSelectionView> selections =
            await modelService.GetSelectionsAsync(goal.Id, cancellationToken);
        string routes = string.Join(" | ", roles.Select(role =>
        {
            GoalModelSelectionView? selection = selections.FirstOrDefault(item => item.Role == role);
            return selection is null
                ? $"{role}: unavailable"
                : $"{role}: {selection.Access} {selection.Provider.Value}/{selection.Model.Value}";
        }));
        using Dialog dialog = new()
        {
            Title = title,
            Width = Dim.Percent(70),
            Height = 11 + (labels.Count * 2),
        };
        TextField[] fields = labels.Select((label, index) =>
            Field(dialog, label, index * 2, "2048")).ToArray();
        Label note = new()
        {
            X = 0,
            Y = labels.Count * 2,
            Width = Dim.Fill(),
            Height = 5,
            Text = "Each role call is capped; the aggregate goal budget is enforced.\n" +
                   routes + "\n" +
                   GoalTextFormatter.FormatCostStatus(goal, cost),
        };
        Label validation = new()
        {
            X = 0,
            Y = (labels.Count * 2) + 5,
            Width = Dim.Fill(),
        };
        MaximumAgentOutputTokens[]? result = null;
        Button run = new() { Title = "_Run" };
        run.Accepting += (_, args) =>
        {
            args.Handled = true;
            int[] values = fields.Select(field =>
                    int.TryParse(field.Text?.ToString(), out int value) ? value : 0)
                .ToArray();
            if (values.Any(value => value is < 1 or > 8192))
            {
                validation.Text = "Every output maximum must be between 1 and 8192 tokens.";
                return;
            }

            result = values.Select(value => new MaximumAgentOutputTokens(value)).ToArray();
            dialog.RequestStop();
        };
        dialog.Add(note, validation);
        dialog.AddButton(run);
        dialog.AddButton(new Button { Title = "_Cancel" });
        await application.RunAsync(dialog, cancellationToken);
        return result;
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
        manageModels.Enabled = enabled && state is not null;
        proposePlan.Enabled = enabled && state is GoalState.Draft or GoalState.NeedsPlanRevision;
        approvePlan.Enabled = enabled && state is GoalState.AwaitingPlanApproval;
        denyPlan.Enabled = enabled && state is GoalState.AwaitingPlanApproval;
        startRun.Enabled = enabled && state is GoalState.Draft or GoalState.NeedsPlanRevision;
        resumeRun.Enabled = enabled && state is not null;
        inspectRun.Enabled = enabled && state is not null;
        manageCommit.Enabled = enabled && state is GoalState.Approved;
    }

    private static TextField Field(Dialog dialog, string label, Pos y, string value)
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
