using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Harness.DataAccess.Mcp;

internal sealed class StatelessHttpMcpToolClient(
    McpConnectionConfigurationOptions options,
    ILoggerFactory loggerFactory) : IMcpToolClient, IAsyncDisposable
{
    internal const int MaximumCatalogTools = 256;
    internal const int MaximumEligibleToolsPerConnection = 32;
    internal const int MaximumDescriptionCharacters = 4_096;
    internal const int MaximumSchemaCharacters = 65_536;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, ActiveConnection> active = new(StringComparer.OrdinalIgnoreCase);
    private McpDiscoverySnapshot current = new([]);

    public McpDiscoverySnapshot Current => current;

    public async ValueTask<McpDiscoverySnapshot> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await DisposeConnectionsAsync();
            List<McpConnectionDiscovery> discoveries = [];
            foreach (McpConnectionConfiguration configuration in options.Connections)
            {
                if (!configuration.IsEnabled)
                {
                    discoveries.Add(new(configuration, null, [], null, null));
                    continue;
                }

                discoveries.Add(await DiscoverAsync(configuration, cancellationToken));
            }

            current = new(discoveries);
            return current;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<McpToolInvocationResult> InvokeAsync(
        McpToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!active.TryGetValue(invocation.Connection.Value, out ActiveConnection? connection) ||
                !connection.Tools.TryGetValue(invocation.Tool.Value, out McpClientTool? tool))
            {
                return Failure("mcp_tool_unavailable",
                    "The MCP tool is not in the current eligible discovery snapshot.");
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(connection.Configuration.RequestTimeout.Value);
            try
            {
                CallToolResult result = await tool.CallAsync(
                    invocation.Arguments,
                    cancellationToken: timeout.Token);
                return new(
                    JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions),
                    result.IsError == true,
                    result.IsError == true ? "mcp_tool_error" : null,
                    ErrorText(result));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failure("mcp_timeout", "The MCP tool call exceeded its request timeout.");
            }
            catch (Exception exception) when (exception is HttpRequestException or McpException or IOException)
            {
                return Failure("mcp_invocation_failed", exception.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            await DisposeConnectionsAsync();
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private async ValueTask<McpConnectionDiscovery> DiscoverAsync(
        McpConnectionConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.Endpoint.Value.Scheme is not ("http" or "https") ||
            configuration.Endpoint.Value.Scheme == "http" && !configuration.Endpoint.Value.IsLoopback)
        {
            return new(configuration, null, [], "mcp_endpoint_unsafe",
                "Remote MCP endpoints must use HTTPS; plain HTTP is allowed only for loopback development servers.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(configuration.RequestTimeout.Value);
        McpClient? client = null;
        bool retained = false;
        try
        {
            HttpClientTransport transport = new(new HttpClientTransportOptions
            {
                Name = configuration.Name.Value,
                Endpoint = configuration.Endpoint.Value,
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = configuration.RequestTimeout.Value,
                EnableStandaloneGetStream = false,
            }, loggerFactory);
            client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = "Harness.NET", Version = "1.0.0" },
                },
                loggerFactory,
                timeout.Token);
            IList<McpClientTool> listed = await client.ListToolsAsync(
                cancellationToken: timeout.Token);
            if (listed.Count > MaximumCatalogTools)
            {
                return new(configuration, client.NegotiatedProtocolVersion, [], "mcp_catalog_too_large",
                    $"The server advertised {listed.Count} tools; the supported maximum is {MaximumCatalogTools}.");
            }

            HashSet<string> duplicateNames = listed
                .GroupBy(tool => tool.Name, StringComparer.Ordinal)
                .Where(group => group.Skip(1).Any())
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            int eligibleCount = 0;
            McpToolDefinition[] definitions = listed.Select(tool => Map(configuration.Name, tool))
                .OrderBy(tool => tool.Name.Value, StringComparer.Ordinal)
                .Select(definition => ApplyCatalogPolicy(definition, duplicateNames, ref eligibleCount))
                .ToArray();
            HashSet<string> eligibleNames = definitions
                .Where(definition => definition.IsAgentEligible)
                .Select(definition => definition.Name.Value)
                .ToHashSet(StringComparer.Ordinal);
            Dictionary<string, McpClientTool> eligible = listed
                .Where(tool => eligibleNames.Contains(tool.Name))
                .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
            active[configuration.Name.Value] = new(configuration, client, eligible);
            retained = true;
            return new(configuration, client.NegotiatedProtocolVersion, definitions, null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(configuration, null, [], "mcp_timeout",
                "Discovery exceeded the configured request timeout.");
        }
        catch (Exception exception) when (exception is HttpRequestException or McpException or IOException or
                                           JsonException or InvalidOperationException or ArgumentException)
        {
            return new(configuration, null, [], "mcp_discovery_failed", exception.Message);
        }
        finally
        {
            if (client is not null && !retained)
            {
                await client.DisposeAsync();
            }
        }
    }

    private async ValueTask DisposeConnectionsAsync()
    {
        foreach (ActiveConnection connection in active.Values)
        {
            await connection.Client.DisposeAsync();
        }

        active.Clear();
    }

    private static McpToolDefinition Map(McpConnectionName connection, McpClientTool tool)
    {
        Tool protocol = tool.ProtocolTool;
        bool readOnly = protocol.Annotations?.ReadOnlyHint == true;
        bool destructive = protocol.Annotations?.DestructiveHint == true;
        bool eligible = IsAgentEligible(
            protocol.Annotations?.ReadOnlyHint,
            protocol.Annotations?.DestructiveHint);
        string? rejection = eligible ? ValidateContextBounds(tool) :
            "Tool must explicitly declare readOnlyHint=true and must not declare destructiveHint=true.";
        eligible &= rejection is null;
        return new(
            connection,
            new(tool.Name),
            tool.Title,
            tool.Description,
            tool.JsonSchema.Clone(),
            tool.ReturnJsonSchema?.Clone(),
            readOnly,
            destructive,
            protocol.Annotations?.OpenWorldHint != false,
            eligible,
            rejection);
    }

    internal static McpToolDefinition ApplyCatalogPolicy(
        McpToolDefinition definition,
        IReadOnlySet<string> duplicateNames,
        ref int eligibleCount)
    {
        if (duplicateNames.Contains(definition.Name.Value))
        {
            return Reject(definition, "The server advertised this tool name more than once.");
        }

        if (!definition.IsAgentEligible)
        {
            return definition;
        }

        eligibleCount++;
        return eligibleCount <= MaximumEligibleToolsPerConnection
            ? definition
            : Reject(definition,
                $"Only the first {MaximumEligibleToolsPerConnection} eligible tools per connection are exposed to agents.");
    }

    private static string? ValidateContextBounds(McpClientTool tool)
    {
        if (tool.Description.Length > MaximumDescriptionCharacters)
        {
            return $"Tool description exceeds {MaximumDescriptionCharacters} characters.";
        }

        if (tool.JsonSchema.GetRawText().Length > MaximumSchemaCharacters ||
            tool.ReturnJsonSchema is { } outputSchema &&
            outputSchema.GetRawText().Length > MaximumSchemaCharacters)
        {
            return $"Tool schema exceeds {MaximumSchemaCharacters} characters.";
        }

        return null;
    }

    private static McpToolDefinition Reject(McpToolDefinition definition, string reason) =>
        definition with { IsAgentEligible = false, RejectionReason = reason };

    internal static bool IsAgentEligible(bool? readOnlyHint, bool? destructiveHint) =>
        readOnlyHint == true && destructiveHint != true;

    private static string? ErrorText(CallToolResult result) =>
        result.IsError == true
            ? string.Join("\n", result.Content.OfType<TextContentBlock>().Select(item => item.Text))
            : null;

    private static McpToolInvocationResult Failure(string code, string error) => new(
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["isError"] = true,
            ["errorCode"] = code,
            ["error"] = error,
        }),
        true,
        code,
        error);

    private sealed record ActiveConnection(
        McpConnectionConfiguration Configuration,
        McpClient Client,
        IReadOnlyDictionary<string, McpClientTool> Tools);
}
