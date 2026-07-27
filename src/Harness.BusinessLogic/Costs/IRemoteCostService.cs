namespace Harness.BusinessLogic.Costs;

public interface IRemoteCostService
{
    ValueTask<RemoteCostReport?> GetAsync(
        string goalId,
        CancellationToken cancellationToken = default);
}
