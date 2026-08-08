using System.Globalization;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;

namespace Harness.Presentation.Terminal;

internal static class GoalTextFormatter
{
    internal static string FormatListItem(GoalView goal) =>
        $"{goal.Title} | {goal.State} | review {goal.ReviewCycleLimit.Value}" +
        SpendLabel(goal);

    internal static string FormatDetails(
        GoalView goal,
        PlanView? plan,
        RemoteCostReport? cost,
        IReadOnlyList<GoalModelSelectionView> selections) => string.Join(
        '\n',
        $"GOAL {goal.State}",
        goal.Title,
        string.Empty,
        goal.Objective,
        string.Empty,
        $"Review-cycle limit: {goal.ReviewCycleLimit.Value}",
        SpendDetail(goal),
        $"Created: {goal.CreatedAt:O}",
        string.Empty,
        plan is null
            ? "PLAN\nNo plan proposed."
            : $"PLAN revision {plan.Revision.Value} | {plan.State}\n{plan.Content}",
        string.Empty,
        FormatSelections(selections),
        string.Empty,
        FormatCostReport(goal, cost));

    internal static string FormatSelections(IReadOnlyList<GoalModelSelectionView> selections) =>
        selections.Count == 0
            ? "ROLE MODELS\nUnavailable"
            : "ROLE MODELS\n" + string.Join('\n', selections.Select(selection =>
                $"{selection.Role,-11} {selection.Provider.Value}/{selection.Model.Value} | " +
                $"{selection.Access} | {(selection.IsExplicit ? "goal-selected" : "configured default")}"));

    internal static string FormatModelCandidate(GoalModelCandidate candidate) =>
        $"{candidate.Access,-6} | {candidate.Provider.Value}/{candidate.Model.Value}" +
        (candidate.InputPrice is null || candidate.OutputPrice is null
            ? " | pricing unavailable"
            : $" | in ${candidate.InputPrice.Value:0.######}/M" +
              $" out ${candidate.OutputPrice.Value:0.######}/M" +
              (candidate.RequestPrice?.Value > 0
                  ? $" req ${candidate.RequestPrice.Value:0.######}"
                  : string.Empty));

    internal static string FormatCostReport(GoalView goal, RemoteCostReport? report)
    {
        RemoteSpendPreference spend = RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget);
        if (spend.Mode is RemoteSpendMode.LocalOnly)
        {
            return "REMOTE COST\nNot authorized; no remote-model spend is permitted.";
        }

        if (report is null)
        {
            return spend.Mode is RemoteSpendMode.Unlimited
                ? "REMOTE COST\nLimit: Unlimited\nNo reservations or charges recorded."
                : "REMOTE COST\nAuthorized cap: $" + FormatUsd(spend.Cap!.Value) +
                  "\nNo reservations or charges recorded.";
        }

        string totals = string.Join(
            '\n',
            "REMOTE COST",
            spend.Mode is RemoteSpendMode.Unlimited
                ? "Limit:      Unlimited"
                : $"Cap:        ${FormatUsd(report.CostCap.Value)}",
            $"Reserved:   ${FormatUsd(report.ReservedCost.Value)}",
            $"Reconciled: ${FormatUsd(report.ReconciledCost.Value)}",
            $"Remaining:  ${FormatUsd(report.RemainingCost.Value)}",
            $"Overage:    ${FormatUsd(report.Overage.Value)}");
        if (report.Items.Count == 0)
        {
            return totals + "\nNo reservations or charges recorded.";
        }

        return totals + "\n\nATTRIBUTION\n" + string.Join('\n', report.Items.Select(item =>
            $"{item.State} | {item.Kind} | {item.Provider}/{item.Model} | " +
            $"estimated ${FormatUsd(item.EstimatedCost.Value)} | " +
            (item.ActualCost is null
                ? "actual pending"
                : $"actual ${FormatUsd(item.ActualCost.Value)}")));
    }

    internal static string FormatCostStatus(GoalView goal, RemoteCostReport? report)
    {
        RemoteSpendPreference spend = RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget);
        if (spend.Mode is RemoteSpendMode.LocalOnly)
        {
            return "Remote spend: not authorized (local-only goal).";
        }

        string limit = spend.Mode is RemoteSpendMode.Unlimited
            ? "Remote spend unlimited"
            : $"Remote cap ${FormatUsd(spend.Cap!.Value)}";
        return report is null
            ? $"{limit} | no spend recorded"
            : $"{limit} | reserved ${FormatUsd(report.ReservedCost.Value)} | " +
              $"spent ${FormatUsd(report.ReconciledCost.Value)}" +
              (spend.Mode is RemoteSpendMode.Capped
                  ? $" | remaining ${FormatUsd(report.RemainingCost.Value)}"
                  : string.Empty);
    }

    internal static string FormatCompact(IReadOnlyList<GoalView> goals) => goals.Count == 0
        ? "GOALS\nNone"
        : "GOALS\n" + string.Join('\n', goals.Take(4).Select(goal =>
            $"{goal.State,-20} {Truncate(goal.Title, 28)}"));

    internal static bool TryParseUsd(string value, out long microUsd)
    {
        microUsd = 0;
        return decimal.TryParse(
                   value,
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out decimal usd) &&
               usd > 0 &&
               TryConvert(usd, out microUsd);
    }

    internal static string FormatUsd(long microUsd) =>
        (microUsd / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);

    private static string SpendLabel(GoalView goal) =>
        RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget) switch
        {
            { Mode: RemoteSpendMode.Unlimited } => " | remote unlimited",
            { Mode: RemoteSpendMode.Capped, Cap: { } cap } =>
                $" | ${FormatUsd(cap.Value)} remote cap",
            _ => " | local only",
        };

    private static string SpendDetail(GoalView goal) =>
        RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget) switch
        {
            { Mode: RemoteSpendMode.Unlimited } => "Remote models: unlimited spend authorized",
            { Mode: RemoteSpendMode.Capped, Cap: { } cap } =>
                $"Remote-model cap: ${FormatUsd(cap.Value)}",
            _ => "Remote models: not authorized",
        };

    private static bool TryConvert(decimal usd, out long microUsd)
    {
        try
        {
            microUsd = checked((long)decimal.Ceiling(usd * 1_000_000m));
            return true;
        }
        catch (OverflowException)
        {
            microUsd = 0;
            return false;
        }
    }

    private static string Truncate(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[..(maximumCharacters - 1)] + "…";
}
