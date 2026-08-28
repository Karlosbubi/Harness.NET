using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

internal sealed partial class GoalWorkflowService
{
    private async ValueTask<HashSet<string>> EvidenceIdsAsync(
        GoalId goalId,
        CancellationToken cancellationToken)
    {
        ToolEvidenceSnapshot evidence = await evidenceService.ListAsync(
            goalId.Value, cancellationToken);
        if (evidence.ErrorCode is not null)
        {
            throw new InvalidOperationException(
                $"Tool evidence is unavailable: {evidence.Error ?? evidence.ErrorCode}");
        }

        return evidence.Items.Select(item => item.Id.Value).ToHashSet(StringComparer.Ordinal);
    }

    private async ValueTask<string> LatestFailedToolFeedbackAsync(
        GoalId goalId,
        CancellationToken cancellationToken)
    {
        const int maximumResultCharacters = 16 * 1024;
        ToolEvidenceSnapshot evidence = await evidenceService.ListAsync(
            goalId.Value, cancellationToken);
        ToolEvidenceView? failed = evidence.ErrorCode is null
            ? evidence.Items
                .Where(item => item.State is ToolEvidenceState.Failed)
                .OrderByDescending(item => item.StartedAt)
                .FirstOrDefault()
            : null;
        if (failed is null)
        {
            return string.Empty;
        }

        string result = failed.ResultJson ?? "No structured failure result was recorded.";
        if (result.Length > maximumResultCharacters)
        {
            result = result[..maximumResultCharacters] + "\n[truncated]";
        }

        return $$"""


            LATEST FAILED TOOL EVIDENCE
            The prior attempt's durable failure follows. Correct these exact diagnostics; do not
            repeat the rejected candidate.
            Tool: {{failed.Tool}}
            Correlation: {{failed.CorrelationId.Value}}
            Result:
            {{result}}
            """;
    }

    private async ValueTask<bool> HasNewDurableEvidenceAsync(
        GoalId goalId, IReadOnlySet<string> previousIds, bool includeVerification,
        CancellationToken cancellationToken)
    {
        ToolEvidenceSnapshot evidence = await evidenceService.ListAsync(
            goalId.Value, cancellationToken);
        return evidence.ErrorCode is null && evidence.Items.Any(item =>
            !previousIds.Contains(item.Id.Value) && item.State is ToolEvidenceState.Succeeded &&
            (item.Tool is ToolKind.FileEdit or ToolKind.Rename or
                ToolKind.DocumentTransformation || includeVerification &&
                item.Tool is ToolKind.Build or ToolKind.Test));
    }
}
