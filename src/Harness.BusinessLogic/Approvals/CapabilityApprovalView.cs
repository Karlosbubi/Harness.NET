using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Approvals;

public sealed record CapabilityApprovalView(
    CapabilityApprovalId Id,
    string GoalId,
    ToolCorrelationId CorrelationId,
    CapabilityKind Capability,
    string Target,
    string Rationale,
    CapabilityApprovalState State,
    string? DecisionReason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);
