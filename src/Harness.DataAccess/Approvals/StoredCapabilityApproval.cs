using Harness.DataAccess.Tools;

namespace Harness.DataAccess.Approvals;

public sealed record StoredCapabilityApproval(
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
