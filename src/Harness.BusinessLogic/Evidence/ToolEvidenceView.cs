using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Evidence;

public sealed record ToolEvidenceView(
    ToolEvidenceId Id,
    string GoalId,
    ToolCorrelationId CorrelationId,
    ToolKind Tool,
    string RequestJson,
    ToolEvidenceState State,
    string? ResultJson,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
