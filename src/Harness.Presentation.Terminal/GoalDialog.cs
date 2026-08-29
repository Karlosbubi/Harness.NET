using System.Collections.ObjectModel;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Workflows;
using Terminal.Gui.App;
using Terminal.Gui.Editor;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Harness.Presentation.Terminal;

internal sealed partial class GoalDialog : Dialog
{
    private readonly IApplication application;
    private readonly IGoalService goalService;
    private readonly IRemoteCostService remoteCostService;
    private readonly IGoalModelService modelService;
    private readonly IGoalWorkflowService workflowService;
    private readonly IGoalAcceptanceService acceptanceService;
    private readonly ISemanticIndexService semanticIndexService;
    private readonly AgentDefaultsSnapshot agentDefaults;
    private readonly string workspaceId;
    private readonly CancellationToken cancellationToken;
    private readonly ListView goalList;
    private readonly Button createGoal;
    private readonly Button inspectGoal;
    private readonly Button manageModels;
    private readonly Button manageContext;
    private readonly Button proposePlan;
    private readonly Button approvePlan;
    private readonly Button denyPlan;
    private readonly Button startRun;
    private readonly Button resumeRun;
    private readonly Button inspectRun;
    private readonly Button manageCommit;
    private readonly Button abortGoal;
    private readonly Label status;
    private IReadOnlyList<GoalView> goals;

    internal GoalDialog(
        IApplication application,
        IGoalService goalService,
        IRemoteCostService remoteCostService,
        IGoalModelService modelService,
        IGoalWorkflowService workflowService,
        IGoalAcceptanceService acceptanceService,
        ISemanticIndexService semanticIndexService,
        AgentDefaultsSnapshot agentDefaults,
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
        this.semanticIndexService = semanticIndexService;
        this.agentDefaults = agentDefaults;
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
        manageContext = CommandButton(
            "_Context",
            Pos.Right(manageModels) + 1,
            Pos.AnchorEnd(8),
            () => ManageContextAsync());
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
        abortGoal = CommandButton(
            "_Abort goal",
            Pos.Right(manageCommit) + 1,
            Pos.AnchorEnd(4),
            () => AbortGoalAsync());
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
            manageContext,
            proposePlan,
            approvePlan,
            denyPlan,
            startRun,
            resumeRun,
            inspectRun,
            manageCommit,
            abortGoal,
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

    private async Task ManageContextAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        SemanticIndexRequest scopedRequest = new(
            workspaceId,
            goal.Id.Value,
            SemanticPrivacyPolicy.NoCollectionAndZeroDataRetention);
        SemanticIndexStatusResult index = await semanticIndexService.GetStatusAsync(
            scopedRequest, cancellationToken);
        RemoteCostReport? cost = await remoteCostService.GetAsync(goal.Id, cancellationToken);
        string current = index.CurrentPartition is null
            ? "No compatible index is ready."
            : $"Ready: {index.CurrentPartition.FileCount} files / " +
              $"{index.CurrentPartition.ChunkCount} chunks / " +
              $"{index.CurrentPartition.CompletedAt:O}";
        int? choice = MessageBox.Query(
            application,
            "Semantic context",
            $"Embedding: {index.Profile.Access} {index.Profile.Provider.Value}/" +
            $"{index.Profile.Model.Value} ({index.Profile.Dimensions.Value} dimensions)\n" +
            current + "\n" + GoalTextFormatter.FormatCostStatus(goal, cost) + "\n" +
            "Rebuild embeds eligible tracked text; Search embeds one bounded query. " +
            "Remote usage is goal-attributed and fails closed at the cap.",
            "_Rebuild",
            "_Search",
            "_Close");
        if (choice == 0)
        {
            int? confirmation = MessageBox.Query(
                application,
                "Confirm semantic rebuild",
                $"Rebuild with {index.Profile.Access} {index.Profile.Provider.Value}/" +
                $"{index.Profile.Model.Value}? The final input size is repository-dependent.\n" +
                GoalTextFormatter.FormatCostStatus(goal, cost),
                "_Rebuild",
                "_Cancel");
            if (confirmation != 0)
            {
                return;
            }

            status.Text = "Rebuilding semantic context; remote batches remain cap-enforced.";
            SemanticIndexResult rebuilt = await semanticIndexService.RebuildAsync(
                scopedRequest, cancellationToken);
            using Dialog result = ReadOnlyDialog(
                "Semantic rebuild result",
                SemanticContextTextFormatter.Format(rebuilt));
            await application.RunAsync(result, cancellationToken);
            status.Text = rebuilt.Error ??
                $"Semantic index ready | {rebuilt.Partition?.ChunkCount} chunks.";
        }
        else if (choice == 1)
        {
            string? query = await CollectMultilineAsync(
                "Preview semantic context", "Query (one attributed embedding call)",
                requireValue: true);
            if (query is null)
            {
                return;
            }

            SemanticSearchResult search = await semanticIndexService.SearchAsync(new(
                workspaceId,
                query,
                MaximumResults: 8,
                goal.Id.Value,
                SemanticPrivacyPolicy.NoCollectionAndZeroDataRetention), cancellationToken);
            using Dialog result = ReadOnlyDialog(
                "Semantic context preview",
                SemanticContextTextFormatter.Format(search));
            await application.RunAsync(result, cancellationToken);
            status.Text = search.Error ??
                $"Semantic preview | {search.Matches.Count} match(es).";
        }
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
            "Remote spend (unlimited, local, or USD cap)",
            12,
            "unlimited");
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
            MicroUsdAmount? budget;
            if (budgetText.Equals("unlimited", StringComparison.OrdinalIgnoreCase))
            {
                budget = RemoteSpendPreference.Default.ToGoalBudget();
            }
            else if (budgetText.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                budget = null;
            }
            else
            {
                if (!GoalTextFormatter.TryParseUsd(budgetText, out long parsedBudget))
                {
                    validation.Text = "Enter unlimited, local, or a positive USD cap.";
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

    private async Task AbortGoalAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        int? choice = MessageBox.Query(
            application,
            "Abort goal",
            $"Abort '{goal.Title}' and return to new-goal creation?\n\n" +
            "History, evidence, and worktree changes are preserved. No files are deleted or undone.",
            "Abort & start new",
            "Keep goal");
        if (choice != 0)
        {
            return;
        }

        await workflowService.AbortAsync(new(
            goal.Id,
            new("Stopped by user to start a different goal.")), cancellationToken);
        await ReloadAsync();
        status.Text = "Goal aborted. Create a new goal when ready.";
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
        manageContext.Enabled = enabled && state is not null;
        proposePlan.Enabled = enabled && state is GoalState.Draft or GoalState.NeedsPlanRevision;
        approvePlan.Enabled = enabled && state is GoalState.AwaitingPlanApproval;
        denyPlan.Enabled = enabled && state is GoalState.AwaitingPlanApproval;
        startRun.Enabled = enabled && state is GoalState.Draft or GoalState.NeedsPlanRevision;
        resumeRun.Enabled = enabled && state is not null;
        inspectRun.Enabled = enabled && state is not null;
        manageCommit.Enabled = enabled && state is GoalState.Approved;
        abortGoal.Enabled = enabled && state is not null;
    }

    private sealed record TerminalPlanGeneration(GoalModelCandidate Model);

    private sealed record TerminalRetry(
        GoalModelCandidate Model,
        GoalRetryGuidance? Guidance);

    private static GoalModelCandidate[] FilterModels(
        IEnumerable<GoalModelCandidate> candidates,
        string? search)
    {
        string value = search?.Trim() ?? string.Empty;
        return candidates.Where(candidate => value.Length == 0 ||
            $"{candidate.Provider.Value} {candidate.Model.Value} {candidate.Access}"
                .Contains(value, StringComparison.OrdinalIgnoreCase)).ToArray();
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
