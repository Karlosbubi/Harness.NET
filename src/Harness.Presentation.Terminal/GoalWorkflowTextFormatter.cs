using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Terminal;

internal static class GoalWorkflowTextFormatter
{
    internal static string Format(GoalWorkflowSnapshot snapshot) => string.Join(
        "\n",
        $"Run:   {snapshot.Id.Value}",
        $"Goal:  {snapshot.GoalId.Value}",
        $"State: {snapshot.State}",
        snapshot.RequiresUserDirection ? "Action: user direction required" : string.Empty,
        string.Empty,
        "ACTIVITY",
        string.Join("\n", snapshot.Activities.Select(item =>
            $"{item.Sequence}. {item.Actor} | {item.Kind} | {item.Summary.Value}")),
        string.Empty,
        "EVIDENCE",
        snapshot.Evidence.Count == 0
            ? "No evidence yet."
            : string.Join("\n\n", snapshot.Evidence.Select(item =>
                $"[{item.Sequence}] {item.Title.Value}\n{item.Content.Value}")));
}
