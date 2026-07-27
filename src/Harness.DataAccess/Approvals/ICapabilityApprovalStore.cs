using Harness.DataAccess.Tools;

namespace Harness.DataAccess.Approvals;

public interface ICapabilityApprovalStore
{
    ValueTask<StoredCapabilityApprovalStart> StartAsync(
        StoredCapabilityApproval approval,
        CancellationToken cancellationToken = default);

    ValueTask<StoredCapabilityApproval> DecideAsync(
        CapabilityApprovalId approvalId,
        CapabilityApprovalState expectedState,
        CapabilityApprovalState nextState,
        string? decisionReason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default);

    ValueTask<StoredCapabilityApproval?> GetByIdAsync(
        CapabilityApprovalId approvalId,
        CancellationToken cancellationToken = default);

    ValueTask<StoredCapabilityApproval?> GetAsync(
        string goalId,
        ToolCorrelationId correlationId,
        CapabilityKind capability,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredCapabilityApproval>> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default);
}
