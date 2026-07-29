using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Evidence;

public interface IRunOutputService
{
    ValueTask<RunOutputSnapshot> ListAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default);
}
