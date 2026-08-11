namespace Harness.DataAccess.Agents;

public sealed record AgentToolExposureConfiguration(IReadOnlyList<string> DirectModuleIds);

public interface IAgentToolExposureConfigurationStore
{
    AgentToolExposureConfiguration Current { get; }
    ValueTask<AgentToolExposureConfiguration> SaveAsync(
        AgentToolExposureConfiguration configuration,
        CancellationToken cancellationToken = default);
}
