using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harness.BusinessLogic.Mcp;
using Harness.DataAccess.Mcp;
using Microsoft.Extensions.AI;

namespace Harness.BusinessLogic.Agents;

internal sealed class McpAgentFunction(
    McpToolDefinition tool,
    IMcpToolService toolService) : AIFunction
{
    private readonly string name = ModelFacingName(tool);

    public override string Name => name;

    public override string Description =>
        $"Read-only MCP tool from {tool.Connection.Value}: {tool.Description}";

    public override JsonElement JsonSchema => tool.InputSchema;

    public override JsonElement? ReturnJsonSchema => tool.OutputSchema;

    public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        Dictionary<string, object?> values = arguments.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.Ordinal);
        McpToolInvocationResult result = await toolService.InvokeAsync(new(
            tool.Connection,
            tool.Name,
            values), cancellationToken);
        using JsonDocument document = JsonDocument.Parse(result.Json);
        return document.RootElement.Clone();
    }

    private static string ModelFacingName(McpToolDefinition tool)
    {
        string raw = $"mcp_{tool.Connection.Value}_{tool.Name.Value}";
        string sanitized = string.Concat(raw.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_'));
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
            .ToLowerInvariant()[..8];
        string prefix = sanitized.Length <= 55 ? sanitized : sanitized[..55];
        return $"{prefix}_{suffix}";
    }
}
