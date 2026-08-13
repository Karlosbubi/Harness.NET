using System.Text.Json;

namespace Harness.DataAccess.Mcp;

public sealed record McpConnectionName(string Value);

public sealed record McpConnectionEndpoint(Uri Value);

public sealed record McpRequestTimeout(TimeSpan Value);

public enum McpConnectionAccess
{
    ReadOnly,
    HarnessControl,
}

public sealed record McpClientIdentifier(string Value);

public sealed record McpConnectionConfiguration(
    McpConnectionName Name,
    McpConnectionEndpoint Endpoint,
    McpRequestTimeout RequestTimeout,
    bool IsEnabled,
    bool RequiresRestart,
    McpConnectionAccess Access = McpConnectionAccess.ReadOnly,
    McpClientIdentifier? ClientId = null,
    IReadOnlyList<McpToolName>? AllowedTools = null);

public sealed record McpConnectionConfigurationOptions(
    IReadOnlyList<McpConnectionConfiguration> Connections);

public sealed record McpToolName(string Value);

public sealed record McpToolDefinition(
    McpConnectionName Connection,
    McpToolName Name,
    string? Title,
    string Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    bool IsReadOnly,
    bool IsDestructive,
    bool IsOpenWorld,
    bool IsAgentEligible,
    string? RejectionReason,
    McpConnectionAccess Access = McpConnectionAccess.ReadOnly);

public sealed record McpConnectionDiscovery(
    McpConnectionConfiguration Configuration,
    string? NegotiatedProtocolVersion,
    IReadOnlyList<McpToolDefinition> Tools,
    string? ErrorCode,
    string? Error);

public sealed record McpDiscoverySnapshot(
    IReadOnlyList<McpConnectionDiscovery> Connections);

public sealed record McpToolInvocation(
    McpConnectionName Connection,
    McpToolName Tool,
    IReadOnlyDictionary<string, object?> Arguments);

public sealed record McpToolInvocationResult(
    string Json,
    bool IsError,
    string? ErrorCode,
    string? Error);

public interface IMcpConnectionConfigurationStore
{
    ValueTask<IReadOnlyList<McpConnectionConfiguration>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<McpConnectionConfiguration> SaveAsync(
        McpConnectionConfiguration configuration,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        McpConnectionName name,
        CancellationToken cancellationToken = default);
}

public interface IMcpToolClient
{
    McpDiscoverySnapshot Current { get; }

    ValueTask<McpDiscoverySnapshot> DiscoverAsync(
        CancellationToken cancellationToken = default);

    ValueTask<McpToolInvocationResult> InvokeAsync(
        McpToolInvocation invocation,
        CancellationToken cancellationToken = default);
}
