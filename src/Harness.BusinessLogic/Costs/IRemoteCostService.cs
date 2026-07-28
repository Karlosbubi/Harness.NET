using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Costs;

public interface IRemoteCostService
{
    ValueTask<RemoteCostReport?> GetAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default);
}
