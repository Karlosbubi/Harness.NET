namespace Harness.BusinessLogic.Approvals;

public sealed record CapabilityApprovalResult(
    CapabilityApprovalView? Approval,
    string? ErrorCode,
    string? Error);
