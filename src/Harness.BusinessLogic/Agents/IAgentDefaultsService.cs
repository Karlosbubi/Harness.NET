namespace Harness.BusinessLogic.Agents;

public interface IAgentDefaultsService
{
    ValueTask<AgentDefaultsSnapshot> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<AgentDefaultsSnapshot> DiscoverAvailableAsync(
        CancellationToken cancellationToken = default);

    ValueTask<AgentRoleDefaultUpdateResult> UpdateAsync(
        AgentRoleDefaultUpdate request,
        CancellationToken cancellationToken = default);
}
