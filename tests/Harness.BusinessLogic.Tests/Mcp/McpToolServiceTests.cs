using System.Text.Json;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Mcp;
using Harness.DataAccess.Mcp;
using DataConnectionName = Harness.DataAccess.Mcp.McpConnectionName;

namespace Harness.BusinessLogic.Tests.Mcp;

public sealed class McpToolServiceTests
{
    [Fact]
    public void Harness_control_tools_are_exposed_only_to_lead()
    {
        McpConnectionConfiguration configuration = new(
            new("worker"),
            new(new Uri("http://127.0.0.1:57431/mcp")),
            new(TimeSpan.FromSeconds(30)),
            IsEnabled: true,
            RequiresRestart: false,
            McpConnectionAccess.HarnessControl);
        McpToolDefinition control = Tool(
            configuration.Name, "harness_create_goal", McpConnectionAccess.HarnessControl);
        McpToolDefinition docs = Tool(
            new("docs"), "lookup", McpConnectionAccess.ReadOnly);
        McpToolService service = new(new FakeClient(new([
            new(configuration, "2026-07-28", [control], null, null),
            new(configuration with { Name = new("docs") }, "2026-07-28", [docs], null, null),
        ])));

        Assert.Equal(
            ["harness_create_goal", "lookup"],
            service.EligibleToolsFor(AgentRole.Lead).Select(tool => tool.Name.Value));
        Assert.Equal(
            ["lookup"],
            service.EligibleToolsFor(AgentRole.Implementer).Select(tool => tool.Name.Value));
        Assert.Equal(
            ["lookup"],
            service.EligibleToolsFor(AgentRole.Reviewer).Select(tool => tool.Name.Value));
    }

    private static McpToolDefinition Tool(
        DataConnectionName connection,
        string name,
        McpConnectionAccess access)
    {
        using JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\"}");
        return new(
            connection,
            new(name),
            null,
            name,
            schema.RootElement.Clone(),
            null,
            IsReadOnly: access is McpConnectionAccess.ReadOnly,
            IsDestructive: false,
            IsOpenWorld: false,
            IsAgentEligible: true,
            RejectionReason: null,
            access);
    }

    private sealed class FakeClient(McpDiscoverySnapshot current) : IMcpToolClient
    {
        public McpDiscoverySnapshot Current { get; } = current;

        public ValueTask<McpDiscoverySnapshot> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);

        public ValueTask<McpToolInvocationResult> InvokeAsync(
            McpToolInvocation invocation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
