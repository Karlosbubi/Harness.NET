using Harness.BusinessLogic.Agents;
using Harness.DataAccess.Agents;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class AgentToolExposureSettingsServiceTests
{
    [Fact]
    public async Task Save_keeps_only_known_available_optional_modules()
    {
        Store store = new();
        AgentToolExposureSettingsService service = new(store);

        AgentToolExposureSettings result = await service.SaveAsync(new(
        [
            new("semantic-hierarchy"),
            new("semantic-hierarchy"),
            new("workspace-inspection"),
            new("unknown"),
        ]));

        Assert.Equal(["semantic-hierarchy"], result.DirectModules.Select(item => item.Value));
        Assert.Equal(["semantic-hierarchy"], store.Current.DirectModuleIds);
    }

    private sealed class Store : IAgentToolExposureConfigurationStore
    {
        public AgentToolExposureConfiguration Current { get; private set; } = new([]);
        public ValueTask<AgentToolExposureConfiguration> SaveAsync(
            AgentToolExposureConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            Current = configuration;
            return ValueTask.FromResult(configuration);
        }
    }
}
