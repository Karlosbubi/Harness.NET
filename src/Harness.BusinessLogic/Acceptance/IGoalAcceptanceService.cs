using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Acceptance;

public interface IGoalAcceptanceService
{
    ValueTask<GoalCommitPreviewResult> PreviewAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default);

    ValueTask<GoalCommitApprovalView?> GetAsync(
        GoalId goalId,
        GoalWorkflowId runId,
        CancellationToken cancellationToken = default);

    ValueTask<GoalCommitApprovalResult> RequestAsync(
        GoalCommitApprovalRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<GoalCommitApprovalResult> DecideAsync(
        GoalCommitDecisionRequest request,
        CancellationToken cancellationToken = default);
}
