namespace Harness.BusinessLogic.Approvals;

public sealed record CapabilityApprovalSnapshot(
    IReadOnlyList<CapabilityApprovalView> Items,
    string? ErrorCode,
    string? Error);
