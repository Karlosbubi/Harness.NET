namespace Harness.BusinessLogic.Approvals;

public interface ICapabilityApprovalService
{
    ValueTask<CapabilityApprovalResult> RequestAsync(
        CapabilityApprovalRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<CapabilityApprovalResult> DecideAsync(
        CapabilityDecisionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<CapabilityApprovalSnapshot> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default);
}
