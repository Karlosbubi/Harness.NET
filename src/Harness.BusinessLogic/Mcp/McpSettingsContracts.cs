namespace Harness.BusinessLogic.Mcp;

public enum McpConnectionState
{
    Disabled,
    Ready,
    Failed,
    RestartRequired,
}

public sealed record McpConnectionName(string Value);

public sealed record McpConnectionEndpoint(string Value);

public sealed record McpTimeoutSeconds(int Value);

public sealed record McpConnectionSettingsView(
    McpConnectionName Name,
    McpConnectionEndpoint Endpoint,
    McpTimeoutSeconds RequestTimeout,
    bool IsEnabled,
    McpConnectionState State,
    string? NegotiatedProtocolVersion,
    int DiscoveredTools,
    int AgentEligibleTools,
    int RejectedTools,
    string? Message,
    bool RequiresRestart);

public sealed record McpSettingsSnapshot(
    IReadOnlyList<McpConnectionSettingsView> Connections);

public sealed record McpConnectionSettingsUpdate(
    McpConnectionName Name,
    McpConnectionEndpoint Endpoint,
    McpTimeoutSeconds RequestTimeout,
    bool IsEnabled);

public sealed record McpSettingsResult(
    McpSettingsSnapshot? Snapshot,
    string? ErrorCode,
    string? Error);

public interface IMcpSettingsService
{
    ValueTask<McpSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    ValueTask<McpSettingsSnapshot> RefreshAsync(CancellationToken cancellationToken = default);

    ValueTask<McpSettingsResult> SaveAsync(
        McpConnectionSettingsUpdate request,
        CancellationToken cancellationToken = default);

    ValueTask<McpSettingsResult> DeleteAsync(
        McpConnectionName name,
        CancellationToken cancellationToken = default);
}
