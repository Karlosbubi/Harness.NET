namespace Harness.DataAccess.Approvals;

public sealed record StoredCapabilityApprovalStart(
    StoredCapabilityApproval Approval,
    bool WasCreated);
