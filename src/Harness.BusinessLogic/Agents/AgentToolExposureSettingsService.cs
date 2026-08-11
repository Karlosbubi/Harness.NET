using Harness.DataAccess.Agents;

namespace Harness.BusinessLogic.Agents;

public sealed record AgentToolExposureSettings(IReadOnlyList<AgentToolModuleId> DirectModules);

public interface IAgentToolExposureSettingsService
{
    ValueTask<AgentToolExposureSettings> GetAsync(CancellationToken cancellationToken = default);
    ValueTask<AgentToolExposureSettings> SaveAsync(
        AgentToolExposureSettings settings, CancellationToken cancellationToken = default);
}

internal sealed class AgentToolExposureSettingsService(
    IAgentToolExposureConfigurationStore store) : IAgentToolExposureSettingsService
{
    public ValueTask<AgentToolExposureSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AgentToolExposureSettings(
            store.Current.DirectModuleIds.Select(value => new AgentToolModuleId(value)).ToArray()));
    }

    public async ValueTask<AgentToolExposureSettings> SaveAsync(
        AgentToolExposureSettings settings, CancellationToken cancellationToken = default)
    {
        HashSet<string> eligible = AgentToolCatalog.Default.Modules.Where(module =>
            module.IsOptional && module.Availability is AgentToolModuleAvailability.Available)
            .Select(module => module.Id.Value).ToHashSet(StringComparer.Ordinal);
        string[] values = settings.DirectModules.Select(item => item.Value)
            .Where(eligible.Contains).Distinct(StringComparer.Ordinal).ToArray();
        AgentToolExposureConfiguration saved = await store.SaveAsync(new(values), cancellationToken);
        return new(saved.DirectModuleIds.Select(value => new AgentToolModuleId(value)).ToArray());
    }
}
