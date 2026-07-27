namespace Harness.BusinessLogic.Workflows;

public interface IWalkingSkeletonWorkflowService
{
    ValueTask<WorkflowSnapshot?> GetLatestAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WorkflowSnapshot> StartAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WorkflowSnapshot> ResumeAsync(
        CancellationToken cancellationToken = default);
}
