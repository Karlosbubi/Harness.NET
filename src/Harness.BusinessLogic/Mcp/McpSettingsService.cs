using System.Text.RegularExpressions;
using Harness.DataAccess.Mcp;
using DataConnectionName = Harness.DataAccess.Mcp.McpConnectionName;

namespace Harness.BusinessLogic.Mcp;

internal sealed partial class McpSettingsService(
    IMcpConnectionConfigurationStore configurationStore,
    IMcpToolClient toolClient) : IMcpSettingsService
{
    public async ValueTask<McpSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default) =>
        await MapAsync(await configurationStore.ListAsync(cancellationToken), toolClient.Current);

    public async ValueTask<McpSettingsSnapshot> RefreshAsync(
        CancellationToken cancellationToken = default) =>
        await MapAsync(
            await configurationStore.ListAsync(cancellationToken),
            await toolClient.DiscoverAsync(cancellationToken));

    public async ValueTask<McpSettingsResult> SaveAsync(
        McpConnectionSettingsUpdate request,
        CancellationToken cancellationToken = default)
    {
        string? validation = Validate(request);
        if (validation is not null)
        {
            return new(null, "invalid_mcp_connection", validation);
        }

        Uri endpoint = new(request.Endpoint.Value, UriKind.Absolute);
        await configurationStore.SaveAsync(new(
            new(request.Name.Value),
            new(endpoint),
            new(TimeSpan.FromSeconds(request.RequestTimeout.Value)),
            request.IsEnabled,
            RequiresRestart: true), cancellationToken);
        return new(await GetAsync(cancellationToken), null, null);
    }

    public async ValueTask<McpSettingsResult> DeleteAsync(
        McpConnectionName name,
        CancellationToken cancellationToken = default)
    {
        if (!ValidName().IsMatch(name.Value))
        {
            return new(null, "invalid_mcp_connection", "Connection name is invalid.");
        }

        bool removed = await configurationStore.DeleteAsync(
            new DataConnectionName(name.Value), cancellationToken);
        return removed
            ? new(await GetAsync(cancellationToken), null, null)
            : new(await GetAsync(cancellationToken), "mcp_connection_not_found",
                $"MCP connection '{name.Value}' was not found.");
    }

    private static ValueTask<McpSettingsSnapshot> MapAsync(
        IReadOnlyList<McpConnectionConfiguration> configurations,
        McpDiscoverySnapshot discovery)
    {
        McpConnectionSettingsView[] views = configurations.Select(configuration =>
        {
            McpConnectionDiscovery? found = discovery.Connections.FirstOrDefault(item =>
                item.Configuration.Name.Value.Equals(
                    configuration.Name.Value, StringComparison.OrdinalIgnoreCase));
            int eligible = found?.Tools.Count(tool => tool.IsAgentEligible) ?? 0;
            int rejected = found?.Tools.Count(tool => !tool.IsAgentEligible) ?? 0;
            McpConnectionState state = configuration.RequiresRestart
                ? McpConnectionState.RestartRequired
                : !configuration.IsEnabled
                    ? McpConnectionState.Disabled
                    : found?.Error is not null
                        ? McpConnectionState.Failed
                        : found is null
                            ? McpConnectionState.Failed
                            : McpConnectionState.Ready;
            return new McpConnectionSettingsView(
                new(configuration.Name.Value),
                new(configuration.Endpoint.Value.AbsoluteUri),
                new((int)configuration.RequestTimeout.Value.TotalSeconds),
                configuration.IsEnabled,
                state,
                found?.NegotiatedProtocolVersion,
                found?.Tools.Count ?? 0,
                eligible,
                rejected,
                found?.Error ?? (configuration.IsEnabled && found is null
                    ? "Connection has not been discovered in this process."
                    : null),
                configuration.RequiresRestart);
        }).ToArray();
        return ValueTask.FromResult(new McpSettingsSnapshot(views));
    }

    private static string? Validate(McpConnectionSettingsUpdate request)
    {
        if (!ValidName().IsMatch(request.Name.Value))
        {
            return "Connection name must start with a letter and contain only letters, digits, underscore, or hyphen (maximum 64 characters).";
        }

        if (!Uri.TryCreate(request.Endpoint.Value, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            return "Endpoint must be an absolute HTTP or HTTPS URI.";
        }

        if (endpoint.Scheme == "http" && !endpoint.IsLoopback)
        {
            return "Remote MCP endpoints must use HTTPS; plain HTTP is allowed only for loopback development servers.";
        }

        return request.RequestTimeout.Value is < 1 or > 3_600
            ? "Request timeout must be between 1 and 3600 seconds."
            : null;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();
}
