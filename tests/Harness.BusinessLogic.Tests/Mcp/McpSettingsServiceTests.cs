using Harness.BusinessLogic.Mcp;
using Harness.DataAccess.Mcp;
using DataConnectionName = Harness.DataAccess.Mcp.McpConnectionName;

namespace Harness.BusinessLogic.Tests.Mcp;

public sealed class McpSettingsServiceTests
{
    [Fact]
    public async Task Saves_valid_https_connection_and_requires_restart()
    {
        MemoryConfigurationStore configurations = new([]);
        FakeToolClient tools = new(new([]));
        McpSettingsService service = new(configurations, tools);

        McpSettingsResult result = await service.SaveAsync(new(
            new("docs"),
            new("https://docs.example.test/mcp"),
            new(45),
            McpConnectionKind.ReadOnly,
            ClientId: null,
            AllowedTools: [],
            IsEnabled: true));

        McpConnectionSettingsView saved = Assert.Single(result.Snapshot!.Connections);
        Assert.Equal(McpConnectionState.RestartRequired, saved.State);
        Assert.True(saved.RequiresRestart);
        Assert.Equal(45, saved.RequestTimeout.Value);
    }

    [Theory]
    [InlineData("bad name", "https://example.test/mcp")]
    [InlineData("docs", "http://example.test/mcp")]
    [InlineData("docs", "file:///tmp/mcp")]
    public async Task Rejects_unsafe_or_invalid_connections(string name, string endpoint)
    {
        McpSettingsService service = new(
            new MemoryConfigurationStore([]),
            new FakeToolClient(new([])));

        McpSettingsResult result = await service.SaveAsync(new(
            new(name), new(endpoint), new(30), McpConnectionKind.ReadOnly,
            ClientId: null, AllowedTools: [], IsEnabled: true));

        Assert.Equal("invalid_mcp_connection", result.ErrorCode);
    }

    [Fact]
    public async Task Maps_discovery_protocol_and_fail_closed_tool_counts()
    {
        McpConnectionConfiguration configuration = Configuration("docs");
        McpToolDefinition eligible = Tool(configuration.Name, "lookup", eligible: true);
        McpToolDefinition rejected = Tool(configuration.Name, "write", eligible: false);
        FakeToolClient tools = new(new([
            new(configuration, "2026-07-28", [eligible, rejected], null, null),
        ]));
        McpSettingsService service = new(
            new MemoryConfigurationStore([configuration]), tools);

        McpSettingsSnapshot snapshot = await service.RefreshAsync();

        McpConnectionSettingsView view = Assert.Single(snapshot.Connections);
        Assert.Equal(McpConnectionState.Ready, view.State);
        Assert.Equal("2026-07-28", view.NegotiatedProtocolVersion);
        Assert.Equal(2, view.DiscoveredTools);
        Assert.Equal(1, view.AgentEligibleTools);
        Assert.Equal(1, view.RejectedTools);
    }

    [Fact]
    public async Task Harness_control_requires_loopback_client_attribution_and_exact_tools()
    {
        MemoryConfigurationStore configurations = new([]);
        McpSettingsService service = new(
            configurations, new FakeToolClient(new([])));

        McpSettingsResult result = await service.SaveAsync(new(
            new("worker"),
            new("http://127.0.0.1:57431/mcp"),
            new(60),
            McpConnectionKind.HarnessControl,
            new("controller"),
            [new("harness_application"), new("harness_create_goal")],
            IsEnabled: true));

        McpConnectionSettingsView saved = Assert.Single(result.Snapshot!.Connections);
        Assert.Equal(McpConnectionKind.HarnessControl, saved.Kind);
        Assert.Equal("controller", saved.ClientId?.Value);
        Assert.Equal(2, saved.AllowedTools.Count);
        Assert.Equal(McpConnectionAccess.HarnessControl,
            configurations.Values.Single().Access);
    }

    private static McpConnectionConfiguration Configuration(string name) => new(
        new(name),
        new(new Uri("https://docs.example.test/mcp")),
        new(TimeSpan.FromSeconds(30)),
        IsEnabled: true,
        RequiresRestart: false);

    private static McpToolDefinition Tool(
        DataConnectionName connection,
        string name,
        bool eligible)
    {
        using System.Text.Json.JsonDocument schema = System.Text.Json.JsonDocument.Parse(
            "{\"type\":\"object\"}");
        return new(
            connection,
            new(name),
            null,
            name,
            schema.RootElement.Clone(),
            null,
            eligible,
            !eligible,
            false,
            eligible,
            eligible ? null : "unsafe");
    }

    private sealed class MemoryConfigurationStore(
        IReadOnlyList<McpConnectionConfiguration> initial) : IMcpConnectionConfigurationStore
    {
        private readonly Dictionary<string, McpConnectionConfiguration> values = initial
            .ToDictionary(item => item.Name.Value, StringComparer.OrdinalIgnoreCase);

        internal IReadOnlyCollection<McpConnectionConfiguration> Values => values.Values;

        public ValueTask<IReadOnlyList<McpConnectionConfiguration>> ListAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<McpConnectionConfiguration>>(values.Values.ToArray());

        public ValueTask<McpConnectionConfiguration> SaveAsync(
            McpConnectionConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            McpConnectionConfiguration saved = configuration with { RequiresRestart = true };
            values[configuration.Name.Value] = saved;
            return ValueTask.FromResult(saved);
        }

        public ValueTask<bool> DeleteAsync(
            DataConnectionName name,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(values.Remove(name.Value));
    }

    private sealed class FakeToolClient(McpDiscoverySnapshot snapshot) : IMcpToolClient
    {
        public McpDiscoverySnapshot Current { get; private set; } = snapshot;

        public ValueTask<McpDiscoverySnapshot> DiscoverAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);

        public ValueTask<McpToolInvocationResult> InvokeAsync(
            McpToolInvocation invocation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
