using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Evidence;

public sealed record RunOutputView(
    ToolEvidenceId Id,
    GoalId GoalId,
    ToolCorrelationId CorrelationId,
    DotNetOperation Operation,
    ToolEvidenceState State,
    DotNetOperationView? Result,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error);
