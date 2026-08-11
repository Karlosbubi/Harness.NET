using Harness.BusinessLogic.Agents;
using Harness.DataAccess.Mcp;

namespace Harness.BusinessLogic.Mcp;

internal interface IMcpToolService
{
    IReadOnlyList<McpToolDefinition> EligibleToolsFor(AgentRole role);

    ValueTask<McpToolInvocationResult> InvokeAsync(
        McpToolInvocation invocation,
        CancellationToken cancellationToken = default);
}

internal sealed class McpToolService(IMcpToolClient client) : IMcpToolService
{
    public IReadOnlyList<McpToolDefinition> EligibleToolsFor(AgentRole role) => client.Current.Connections
        .SelectMany(connection => connection.Tools)
        .Where(tool => tool.IsAgentEligible &&
            (tool.Access is McpConnectionAccess.ReadOnly || role is AgentRole.Lead))
        .ToArray();

    public ValueTask<McpToolInvocationResult> InvokeAsync(
        McpToolInvocation invocation,
        CancellationToken cancellationToken = default) =>
        client.InvokeAsync(invocation, cancellationToken);
}
