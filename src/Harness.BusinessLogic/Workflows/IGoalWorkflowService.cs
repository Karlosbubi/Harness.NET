using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public interface IGoalWorkflowService
{
    ValueTask<GoalWorkflowSnapshot?> GetLatestAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<GoalWorkflowSnapshot> StartPlanningAsync(
        GoalWorkflowStartRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<GoalWorkflowSnapshot> ResumeAsync(
        GoalWorkflowResumeRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<GoalWorkflowSnapshot> RetryAsync(
        GoalWorkflowRetryRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<GoalWorkflowSnapshot> AbortAsync(
        GoalWorkflowAbortRequest request,
        CancellationToken cancellationToken = default);
}
