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
        RemoteSpendPreference spend = RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget);
        if (spend.Mode is RemoteSpendMode.LocalOnly)
        {
            costText = "REMOTE COST\nNot authorized; no remote-model spend is permitted.";
        }
        else if (cost is null)
        {
            costText = spend.Mode is RemoteSpendMode.Unlimited
                ? "REMOTE COST\nLimit: Unlimited\nNo reservations or charges recorded."
                : $"REMOTE COST\nCap: ${ToUsd(spend.Cap!.Value)}\n" +
                       "No reservations or charges recorded.";
        }
        else
        {
            List<string> costLines =
            [
                "REMOTE COST",
                spend.Mode is RemoteSpendMode.Unlimited
                    ? "Limit:      Unlimited"
                    : $"Cap:        ${ToUsd(cost.CostCap.Value)}",
                $"Reserved:   ${ToUsd(cost.ReservedCost.Value)}",
                $"Reconciled: ${ToUsd(cost.ReconciledCost.Value)}",
            ];
            if (spend.Mode is RemoteSpendMode.Capped)
            {
                costLines.Add($"Remaining:  ${ToUsd(cost.RemainingCost.Value)}");
            }
            costLines.Add($"Overage:    ${ToUsd(cost.Overage.Value)}");
            costText = string.Join('\n', costLines);
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

    internal static string RetryDisclosure(
        GoalWorkflowRetryRole retryRole,
        GoalManagementState state) => string.Join(
        '\n',
        $"This explicitly starts a new {retryRole} call from the last durable safe boundary.",
        "The prior call is not replayed automatically and may already have incurred remote cost. " +
        "Inspect its recovery notice and durable tool evidence before retrying.",
        "Typed mutation baselines still reject stale repository writes; the goal spend policy always applies.",
        FormatSelections(state.ModelSelections),
        FormatCostSummary(state));

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
        if (goal is null)
        {
            return "Remote spend: no goal selected.";
        }

        RemoteSpendPreference spend = RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget);
        if (spend.Mode is RemoteSpendMode.LocalOnly)
        {
            return "Remote spend: not authorized (local-only goal).";
        }

        RemoteCostReport? cost = state.Cost;
        string limit = spend.Mode is RemoteSpendMode.Unlimited
            ? "Remote spend unlimited"
            : $"Remote cap ${ToUsd(spend.Cap!.Value)}";
        return cost is null
            ? $"{limit} | no spend recorded"
            : $"{limit} | reserved ${ToUsd(cost.ReservedCost.Value)} | " +
              $"spent ${ToUsd(cost.ReconciledCost.Value)}" +
              (spend.Mode is RemoteSpendMode.Capped
                  ? $" | remaining ${ToUsd(cost.RemainingCost.Value)}"
                  : string.Empty);
    }

    internal static string FormatCostSummary(GoalView goal, RemoteCostReport? cost)
    {
        RemoteSpendPreference spend = RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget);
        if (spend.Mode is RemoteSpendMode.LocalOnly)
        {
            return "Remote spend: not authorized (local-only goal).";
        }

        string limit = spend.Mode is RemoteSpendMode.Unlimited
            ? "Remote spend unlimited"
            : $"Remote cap ${ToUsd(spend.Cap!.Value)}";
        return cost is null
            ? $"{limit} | no spend recorded"
            : $"{limit} | reserved ${ToUsd(cost.ReservedCost.Value)} | " +
              $"spent ${ToUsd(cost.ReconciledCost.Value)}" +
              (spend.Mode is RemoteSpendMode.Capped
                  ? $" | remaining ${ToUsd(cost.RemainingCost.Value)}"
                  : string.Empty);
    }

    private static string FormatEmbeddingCost(EmbeddingUsageView usage) => usage.Cost is null
        ? "$0.000000"
        : $"${usage.Cost.Value / 1_000_000m:F6}";
}
