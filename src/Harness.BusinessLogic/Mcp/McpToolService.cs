using Harness.DataAccess.Mcp;

namespace Harness.BusinessLogic.Mcp;

internal interface IMcpToolService
{
    IReadOnlyList<McpToolDefinition> EligibleTools { get; }

    ValueTask<McpToolInvocationResult> InvokeAsync(
        McpToolInvocation invocation,
        CancellationToken cancellationToken = default);
}

internal sealed class McpToolService(IMcpToolClient client) : IMcpToolService
{
    public IReadOnlyList<McpToolDefinition> EligibleTools => client.Current.Connections
        .SelectMany(connection => connection.Tools)
        .Where(tool => tool.IsAgentEligible)
        .ToArray();

    public ValueTask<McpToolInvocationResult> InvokeAsync(
        McpToolInvocation invocation,
        CancellationToken cancellationToken = default) =>
        client.InvokeAsync(invocation, cancellationToken);
}
