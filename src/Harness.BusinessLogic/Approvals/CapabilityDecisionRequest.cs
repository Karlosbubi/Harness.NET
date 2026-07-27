namespace Harness.BusinessLogic.Approvals;

public sealed record CapabilityDecisionRequest(
    CapabilityApprovalId ApprovalId,
    CapabilityDecision Decision,
    string? Reason);
