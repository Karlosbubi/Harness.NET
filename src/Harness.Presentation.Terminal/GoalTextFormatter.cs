using System.Globalization;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;

namespace Harness.Presentation.Terminal;

internal static class GoalTextFormatter
{
    internal static string FormatListItem(GoalView goal) =>
        $"{goal.Title} | {goal.State} | review {goal.ReviewCycleLimit.Value}" +
        (goal.RemoteBudget is null
            ? " | local only"
            : $" | ${ToUsd(goal.RemoteBudget.Value)} remote cap");

    internal static string FormatDetails(
        GoalView goal,
        PlanView? plan,
        RemoteCostReport? cost) => string.Join(
        '\n',
        $"GOAL {goal.State}",
        goal.Title,
        string.Empty,
        goal.Objective,
        string.Empty,
        $"Review-cycle limit: {goal.ReviewCycleLimit.Value}",
        goal.RemoteBudget is null
            ? "Remote models: not authorized"
            : $"Remote-model cap: ${ToUsd(goal.RemoteBudget.Value)}",
        $"Created: {goal.CreatedAt:O}",
        string.Empty,
        plan is null
            ? "PLAN\nNo plan proposed."
            : $"PLAN revision {plan.Revision.Value} | {plan.State}\n{plan.Content}",
        string.Empty,
        FormatCostReport(goal, cost));

    internal static string FormatCostReport(GoalView goal, RemoteCostReport? report)
    {
        if (goal.RemoteBudget is null)
        {
            return "REMOTE COST\nNot authorized; no remote-model spend is permitted.";
        }

        if (report is null)
        {
            return "REMOTE COST\nAuthorized cap: $" + ToUsd(goal.RemoteBudget.Value) +
                   "\nNo reservations or charges recorded.";
        }

        string totals = string.Join(
            '\n',
            "REMOTE COST",
            $"Cap:        ${ToUsd(report.CostCap.Value)}",
            $"Reserved:   ${ToUsd(report.ReservedCost.Value)}",
            $"Reconciled: ${ToUsd(report.ReconciledCost.Value)}",
            $"Remaining:  ${ToUsd(report.RemainingCost.Value)}",
            $"Overage:    ${ToUsd(report.Overage.Value)}");
        if (report.Items.Count == 0)
        {
            return totals + "\nNo reservations or charges recorded.";
        }

        return totals + "\n\nATTRIBUTION\n" + string.Join('\n', report.Items.Select(item =>
            $"{item.State} | {item.Kind} | {item.Provider}/{item.Model} | " +
            $"estimated ${ToUsd(item.EstimatedCost.Value)} | " +
            (item.ActualCost is null
                ? "actual pending"
                : $"actual ${ToUsd(item.ActualCost.Value)}")));
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

    private static string ToUsd(long microUsd) =>
        (microUsd / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);

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
