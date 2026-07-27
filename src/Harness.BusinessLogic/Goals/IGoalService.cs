namespace Harness.BusinessLogic.Goals;

public interface IGoalService
{
    ValueTask<GoalResult> CreateAsync(
        GoalCreateRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<GoalView?> GetAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<GoalView>> ListAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    ValueTask<PlanView?> GetCurrentPlanAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    ValueTask<PlanResult> ProposePlanAsync(
        PlanProposalRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PlanResult> DecidePlanAsync(
        PlanDecisionRequest request,
        CancellationToken cancellationToken = default);
}
