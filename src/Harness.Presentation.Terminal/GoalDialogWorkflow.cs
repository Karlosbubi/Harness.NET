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

internal sealed partial class GoalDialog
{
    private async Task StartRunAsync()
    {
        GoalView? goal = SelectedGoal();
        if (goal is null)
        {
            status.Text = "Select a goal.";
            return;
        }

        TerminalPlanGeneration? generation = await CollectPlanGenerationAsync(goal);
        if (generation is null)
        {
            return;
        }

        if (generation.Model.Access is ModelAccess.Remote)
        {
            if (goal.RemoteBudget is null)
            {
                status.Text = "This goal is local-only. Choose unlimited or capped remote spend before using this route.";
                return;
            }

            RemoteCostReport? cost = await remoteCostService.GetAsync(goal.Id, cancellationToken);
            int? confirmation = MessageBox.Query(
                application,
                "Authorize remote Lead model",
                $"Use {generation.Model.Provider.Value}/{generation.Model.Model.Value} for plan generation?\n" +
                $"{GoalTextFormatter.FormatCostStatus(goal, cost)}\n" +
                "This selection remains governed by the goal spend policy.",
                "_Authorize",
                "_Cancel");
            if (confirmation != 0)
            {
                return;
            }
        }

        GoalModelSelectionResult selected = await modelService.SelectAsync(new(
            goal.Id,
            AgentRole.Lead,
            generation.Model.Provider,
            generation.Model.Model), cancellationToken);
        if (selected.Selection is null)
        {
            status.Text = selected.Error ?? "Lead model selection failed.";
            return;
        }

        GoalWorkflowSnapshot? latest = null;
        await foreach (GoalWorkflowSnapshot snapshot in workflowService.StartPlanningAsync(
                           new(goal.Id), cancellationToken))
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

        GoalWorkflowSnapshot? current = await workflowService.GetLatestAsync(
            goal.Id, cancellationToken);
        if (current?.RetryRole is { } retryRole)
        {
            AgentRole agentRole = retryRole switch
            {
                GoalWorkflowRetryRole.Lead => AgentRole.Lead,
                GoalWorkflowRetryRole.Implementer => AgentRole.Implementer,
                GoalWorkflowRetryRole.Reviewer => AgentRole.Reviewer,
                _ => throw new ArgumentOutOfRangeException(nameof(retryRole)),
            };
            TerminalRetry? retry = await CollectRetryAsync(goal, retryRole, agentRole);
            if (retry is null)
            {
                return;
            }

            if (retry.Model.Access is ModelAccess.Remote)
            {
                if (goal.RemoteBudget is null)
                {
                    status.Text = "This goal is local-only. Choose unlimited or capped remote spend before using this route.";
                    return;
                }

                RemoteCostReport? cost = await remoteCostService.GetAsync(goal.Id, cancellationToken);
                int? confirmation = MessageBox.Query(
                    application,
                    $"Authorize remote {retryRole} retry",
                    $"Use {retry.Model.Provider.Value}/{retry.Model.Model.Value} for this retry?\n" +
                    $"{GoalTextFormatter.FormatCostStatus(goal, cost)}\n" +
                    "The prior call is not replayed and the goal spend policy still applies.",
                    "_Authorize",
                    "_Cancel");
                if (confirmation != 0)
                {
                    return;
                }
            }

            GoalModelSelectionResult selected = await modelService.SelectAsync(new(
                goal.Id, agentRole, retry.Model.Provider, retry.Model.Model), cancellationToken);
            if (selected.Selection is null)
            {
                status.Text = selected.Error ?? "Retry model selection failed.";
                return;
            }

            GoalWorkflowSnapshot? retried = null;
            await foreach (GoalWorkflowSnapshot snapshot in workflowService.RetryAsync(
                               new(goal.Id, retryRole, retry.Guidance), cancellationToken))
            {
                retried = snapshot;
                status.Text = $"Run {snapshot.State} | {snapshot.Activities[^1].Kind}";
            }

            if (retried is not null)
            {
                await ShowRunAsync(retried);
            }

            return;
        }

        GoalWorkflowSnapshot? latest = null;
        await foreach (GoalWorkflowSnapshot snapshot in workflowService.ResumeAsync(
                           new(goal.Id), cancellationToken))
        {
            latest = snapshot;
            status.Text = $"Run {snapshot.State} | {snapshot.Activities[^1].Kind}";
        }

        if (latest is not null)
        {
            await ShowRunAsync(latest);
        }
    }

    private async Task<GoalBudgetExtensionRequest?> CollectBudgetExtensionAsync(GoalView goal)
    {
        using Dialog dialog = new()
        {
            Title = "Increase remote cap",
            Width = Dim.Percent(75),
            Height = 15,
        };
        TextField newBudget = Field(dialog, "New total cap (USD)", 0, string.Empty);
        Editor reason = new()
        {
            X = 0,
            Y = 4,
            Width = Dim.Fill(),
            Height = 5,
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar,
        };
        dialog.Add(new Label { Text = "Required reason", X = 0, Y = 3 }, reason);
        Label validation = new() { X = 0, Y = 9, Width = Dim.Fill(), Height = 2 };
        GoalBudgetExtensionRequest? result = null;
        Button approve = new() { Title = "_Increase cap" };
        approve.Accepting += (_, args) =>
        {
            args.Handled = true;
            if (!GoalTextFormatter.TryParseUsd(
                    newBudget.Text?.ToString() ?? string.Empty, out long parsedBudget) ||
                parsedBudget <= (goal.RemoteBudget?.Value ?? 0))
            {
                validation.Text = "The new total cap must be a valid USD amount above the current cap.";
                return;
            }

            string explanation = reason.Text?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(explanation) || explanation.Length > 2_000)
            {
                validation.Text = "A 1-2000 character reason is required.";
                return;
            }

            result = new(
                goal.Id,
                goal.RemoteBudget,
                new(parsedBudget),
                new(explanation));
            dialog.RequestStop();
        };
        dialog.Add(validation);
        dialog.AddButton(approve);
        dialog.AddButton(new Button { Title = "_Cancel" });
        await application.RunAsync(dialog, cancellationToken);
        return result;
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
        if (committed.Error is not null || committed.Approval is null)
        {
            status.Text = committed.Error ?? "The exact commit did not complete.";
            return;
        }

        status.Text = $"Goal branch ready | {committed.Approval.Branch.Value} | " +
                      committed.Approval.CommitSha?.Value;
        using Dialog handoff = ReadOnlyDialog(
            "Goal branch ready",
            GoalCommitTextFormatter.FormatHandoff(committed.Approval));
        await application.RunAsync(handoff, cancellationToken);
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

    private async Task<TerminalPlanGeneration?> CollectPlanGenerationAsync(GoalView goal)
    {
        GoalModelCandidate[] candidates = agentDefaults.Models
            .Where(candidate => candidate.SupportedRoles.Contains(AgentRole.Lead))
            .ToArray();
        IReadOnlyList<GoalModelSelectionView> selections =
            await modelService.GetSelectionsAsync(goal.Id, cancellationToken);
        GoalModelSelectionView? effective = selections.FirstOrDefault(selection =>
            selection.Role is AgentRole.Lead);
        AgentRoleDefault? configured = agentDefaults.Roles.FirstOrDefault(roleDefault =>
            roleDefault.Role is AgentRole.Lead);
        int preferred = Array.FindIndex(candidates, candidate =>
            candidate.Provider == effective?.Provider && candidate.Model == effective?.Model);
        if (preferred < 0)
        {
            preferred = Array.FindIndex(candidates, candidate =>
                candidate.Provider == configured?.Provider && candidate.Model == configured?.Model);
        }

        using Dialog dialog = new()
        {
            Title = "Generate goal plan",
            Width = Dim.Percent(80),
            Height = 20,
        };
        dialog.Add(new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = "Lead model — search provider/model below and press Enter (role-compatible only)",
        });
        TextField search = new()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Text = string.Empty,
        };
        GoalModelCandidate[] visibleCandidates = candidates;
        ListView models = new()
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = 6,
            SelectedItem = preferred >= 0 ? preferred : 0,
        };
        models.SetSource(new ObservableCollection<string>(visibleCandidates.Select(candidate =>
            $"{candidate.Access} {candidate.Provider.Value}/{candidate.Model.Value}")));
        search.Accepting += (_, args) =>
        {
            args.Handled = true;
            visibleCandidates = FilterModels(candidates, search.Text?.ToString());
            models.SetSource(new ObservableCollection<string>(visibleCandidates.Select(candidate =>
                $"{candidate.Access} {candidate.Provider.Value}/{candidate.Model.Value}")));
            models.SelectedItem = visibleCandidates.Length > 0 ? 0 : null;
        };
        Label validation = new() { X = 0, Y = 9, Width = Dim.Fill(), Height = 2 };
        TerminalPlanGeneration? result = null;
        Button run = new() { Title = "_Generate", Enabled = candidates.Length > 0 };
        run.Accepting += (_, args) =>
        {
            args.Handled = true;
            int index = models.SelectedItem ?? -1;
            if (index < 0 || index >= visibleCandidates.Length)
            {
                validation.Text = "No fully compatible Lead model is available.";
                return;
            }

            result = new(visibleCandidates[index]);
            dialog.RequestStop();
        };
        dialog.Add(search, models, validation);
        dialog.AddButton(run);
        dialog.AddButton(new Button { Title = "_Cancel" });
        await application.RunAsync(dialog, cancellationToken);
        return result;
    }

    private async Task<TerminalRetry?> CollectRetryAsync(
        GoalView goal,
        GoalWorkflowRetryRole retryRole,
        AgentRole role)
    {
        GoalModelCandidate[] candidates = agentDefaults.Models
            .Where(candidate => candidate.SupportedRoles.Contains(role))
            .ToArray();
        IReadOnlyList<GoalModelSelectionView> selections =
            await modelService.GetSelectionsAsync(goal.Id, cancellationToken);
        GoalModelSelectionView? effective = selections.FirstOrDefault(selection =>
            selection.Role == role);
        AgentRoleDefault? configured = agentDefaults.Roles.FirstOrDefault(roleDefault =>
            roleDefault.Role == role);
        int preferred = Array.FindIndex(candidates, candidate =>
            candidate.Provider == effective?.Provider && candidate.Model == effective?.Model);
        if (preferred < 0)
        {
            preferred = Array.FindIndex(candidates, candidate =>
                candidate.Provider == configured?.Provider && candidate.Model == configured?.Model);
        }

        using Dialog dialog = new()
        {
            Title = $"Retry {retryRole} with changes",
            Width = Dim.Percent(80),
            Height = 24,
        };
        dialog.Add(new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = "Replacement model — search provider/model below and press Enter (role-compatible only)",
        });
        TextField search = new()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Text = string.Empty,
        };
        GoalModelCandidate[] visibleCandidates = candidates;
        ListView models = new()
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = 5,
            SelectedItem = preferred >= 0 ? preferred : 0,
        };
        models.SetSource(new ObservableCollection<string>(visibleCandidates.Select(candidate =>
            $"{candidate.Access} {candidate.Provider.Value}/{candidate.Model.Value}")));
        search.Accepting += (_, args) =>
        {
            args.Handled = true;
            visibleCandidates = FilterModels(candidates, search.Text?.ToString());
            models.SetSource(new ObservableCollection<string>(visibleCandidates.Select(candidate =>
                $"{candidate.Access} {candidate.Provider.Value}/{candidate.Model.Value}")));
            models.SelectedItem = visibleCandidates.Length > 0 ? 0 : null;
        };
        dialog.Add(new Label { X = 0, Y = 8, Text = "Additional guidance (optional)" });
        Editor guidance = new()
        {
            X = 0,
            Y = 9,
            Width = Dim.Fill(),
            Height = 5,
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar,
        };
        Label validation = new() { X = 0, Y = 15, Width = Dim.Fill(), Height = 2 };
        TerminalRetry? result = null;
        Button run = new() { Title = "_Retry", Enabled = candidates.Length > 0 };
        run.Accepting += (_, args) =>
        {
            args.Handled = true;
            int index = models.SelectedItem ?? -1;
            string direction = guidance.Text?.ToString()?.Trim() ?? string.Empty;
            if (index < 0 || index >= visibleCandidates.Length)
            {
                validation.Text = "No fully compatible replacement model is available.";
                return;
            }

            if (direction.Length > 16 * 1024)
            {
                validation.Text = "Optional retry guidance may contain at most 16384 characters.";
                return;
            }

            result = new(
                visibleCandidates[index],
                direction.Length == 0 ? null : new(direction));
            dialog.RequestStop();
        };
        dialog.Add(search, models, guidance, validation);
        dialog.AddButton(run);
        dialog.AddButton(new Button { Title = "_Cancel" });
        await application.RunAsync(dialog, cancellationToken);
        return result;
    }

}
