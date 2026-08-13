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
        Map(await configurationStore.ListAsync(cancellationToken), toolClient.Current);

    public async ValueTask<McpSettingsSnapshot> RefreshAsync(
        CancellationToken cancellationToken = default) =>
        Map(
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
            RequiresRestart: true,
            Access: request.Kind is McpConnectionKind.HarnessControl
                ? McpConnectionAccess.HarnessControl
                : McpConnectionAccess.ReadOnly,
            ClientId: request.ClientId is null ? null : new(request.ClientId.Value),
            AllowedTools: request.AllowedTools.Select(tool =>
                new McpToolName(tool.Value)).ToArray()), cancellationToken);
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

    private static McpSettingsSnapshot Map(
        IReadOnlyList<McpConnectionConfiguration> configurations,
        McpDiscoverySnapshot discovery)
    {
        List<McpConnectionSettingsView> views = [];
        foreach (McpConnectionConfiguration configuration in configurations)
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
            views.Add(new McpConnectionSettingsView(
                new(configuration.Name.Value),
                new(configuration.Endpoint.Value.AbsoluteUri),
                new((int)configuration.RequestTimeout.Value.TotalSeconds),
                configuration.Access is McpConnectionAccess.HarnessControl
                    ? McpConnectionKind.HarnessControl
                    : McpConnectionKind.ReadOnly,
                configuration.ClientId is null ? null : new(configuration.ClientId.Value),
                (configuration.AllowedTools ?? []).Select(tool =>
                    new McpAllowedToolName(tool.Value)).ToArray(),
                configuration.IsEnabled,
                state,
                found?.NegotiatedProtocolVersion,
                found?.Tools.Count ?? 0,
                eligible,
                rejected,
                found?.Error ?? (configuration.IsEnabled && found is null
                    ? "Connection has not been discovered in this process."
                    : null),
                configuration.RequiresRestart));
        }
        return new(views);
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

        if (request.Kind is McpConnectionKind.HarnessControl)
        {
            if (!endpoint.IsLoopback)
                return "Harness control connections are limited to loopback endpoints.";
            if (request.ClientId is null || !ValidClientId().IsMatch(request.ClientId.Value))
                return "Harness control client ID must contain 1-128 letters, digits, dot, underscore, or hyphen characters.";
            if (request.AllowedTools.Count is < 1 or > 32 ||
                request.AllowedTools.Any(tool =>
                    !ValidHarnessTool().IsMatch(tool.Value)) ||
                request.AllowedTools.Select(tool => tool.Value)
                    .Distinct(StringComparer.Ordinal).Count() != request.AllowedTools.Count)
                return "Harness control requires 1-32 distinct harness_ tool IDs.";
        }
        else if (request.ClientId is not null || request.AllowedTools.Count > 0)
        {
            return "Read-only MCP connections cannot configure Harness control credentials or tool grants.";
        }

        return request.RequestTimeout.Value is < 1 or > 3_600
            ? "Request timeout must be between 1 and 3600 seconds."
            : null;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidClientId();

    [GeneratedRegex("^harness_[a-z0-9_]{1,120}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidHarnessTool();

}
