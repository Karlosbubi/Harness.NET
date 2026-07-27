using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Terminal;

internal static class WorkflowTextFormatter
{
    internal static string FormatActivity(WorkflowSnapshot snapshot) => string.Join(
        "\n\n",
        snapshot.Activities.Select(activity =>
            $"{activity.Sequence}. {activity.Actor} [{activity.Stage}]\n{activity.Summary.Value}"));

    internal static string FormatEvidence(WorkflowSnapshot snapshot) => snapshot.Evidence.Count == 0
        ? "No evidence has been persisted yet."
        : string.Join(
            "\n\n",
            snapshot.Evidence.Select(evidence =>
                $"CHECKPOINT {evidence.Sequence}: {evidence.Title.Value}\n{evidence.Content.Value}"));
}
