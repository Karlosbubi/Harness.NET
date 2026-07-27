using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Approvals;

public sealed record CapabilityApprovalRequest(
    string GoalId,
    ToolCorrelationId CorrelationId,
    CapabilityKind Capability,
    string Rationale);
