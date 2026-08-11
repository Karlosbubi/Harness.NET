using System.Text.Json;
using Harness.DataAccess.Mcp;

namespace Harness.DataAccess.Research;

internal sealed class McpDocumentationSource(
    IMcpToolClient toolClient,
    IResearchSettingsStore settingsStore,
    TimeProvider timeProvider) : IDocumentationSource
{
    public DocumentationSourceId Id { get; } = new("configured-mcp-documentation");

    public DocumentationSourceClass SourceClass => DocumentationSourceClass.Mcp;

    public async ValueTask<DocumentationSourceResult> SearchAsync(
        DocumentationSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        ResearchSourceSettings settings = await settingsStore.GetAsync(cancellationToken);
        List<DocumentationSourceMatch> matches = [];
        List<string> failures = [];
        foreach (DocumentationMcpToolRoute route in settings.McpTools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            McpToolDefinition? definition = toolClient.Current.Connections
                .Where(connection => connection.Configuration.Name.Value.Equals(
                    route.Connection, StringComparison.OrdinalIgnoreCase))
                .SelectMany(connection => connection.Tools)
                .FirstOrDefault(tool => tool.Name.Value.Equals(route.Tool,
                    StringComparison.OrdinalIgnoreCase));
            if (definition is null || !definition.IsAgentEligible || !definition.IsReadOnly ||
                definition.IsDestructive || definition.IsOpenWorld)
            {
                failures.Add($"{route.Connection}/{route.Tool}: tool is unavailable or not closed read-only");
                continue;
            }
            McpToolInvocationResult result = await toolClient.InvokeAsync(new(
                new(route.Connection),
                new(route.Tool),
                new Dictionary<string, object?>
                {
                    ["library"] = query.Library.Value,
                    ["version"] = query.Version?.Value,
                    ["query"] = query.Query.Value,
                    ["maximumResults"] = query.MaximumResults,
                    ["maximumCharacters"] = query.MaximumCharacters,
                }), cancellationToken);
            if (result.IsError)
            {
                failures.Add($"{route.Connection}/{route.Tool}: {result.Error ?? result.ErrorCode}");
                continue;
            }
            try
            {
                matches.AddRange(Parse(
                    result.Json,
                    route,
                    query,
                    timeProvider.GetUtcNow()));
            }
            catch (JsonException exception)
            {
                failures.Add($"{route.Connection}/{route.Tool}: {exception.Message}");
            }
        }
        DocumentationSourceMatch[] ranked = DocumentationFileSearch.Rank(
            matches, query.MaximumResults, query.MaximumCharacters);
        return new(
            Id,
            SourceClass,
            ranked,
            ranked.Any(match => match.IsExactVersion && match.Confidence >= 0.7m),
            failures.Count > 0 ? "mcp_documentation_incomplete" :
                ranked.Length == 0 ? "mcp_documentation_no_match" : null,
            failures.Count > 0 ? string.Join("; ", failures) :
                ranked.Length == 0 ? "No configured MCP documentation tool returned a match." : null);
    }

    private static IReadOnlyList<DocumentationSourceMatch> Parse(
        string json,
        DocumentationMcpToolRoute route,
        DocumentationSourceQuery query,
        DateTimeOffset retrievedAt)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        JsonElement value = Unwrap(document.RootElement);
        List<DocumentationSourceMatch> matches = [];
        ParseValue(value, route, query, retrievedAt, matches);
        return matches;
    }

    private static JsonElement Unwrap(JsonElement root)
    {
        if (root.TryGetProperty("structuredContent", out JsonElement structured) &&
            structured.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return structured;
        }
        if (root.TryGetProperty("content", out JsonElement content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in content.EnumerateArray())
            {
                if (String(item, "text") is { } text)
                {
                    try
                    {
                        using JsonDocument nested = JsonDocument.Parse(text);
                        return nested.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        return item;
                    }
                }
            }
        }
        return root;
    }

    private static void ParseValue(
        JsonElement value,
        DocumentationMcpToolRoute route,
        DocumentationSourceQuery query,
        DateTimeOffset retrievedAt,
        ICollection<DocumentationSourceMatch> output)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("results",
                out JsonElement results))
        {
            value = results;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                Add(item, route, query, retrievedAt, output);
            }
        }
        else
        {
            Add(value, route, query, retrievedAt, output);
        }
    }

    private static void Add(
        JsonElement item,
        DocumentationMcpToolRoute route,
        DocumentationSourceQuery query,
        DateTimeOffset retrievedAt,
        ICollection<DocumentationSourceMatch> output)
    {
        string? content = String(item, "content") ?? String(item, "text") ??
            String(item, "description");
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }
        string? version = String(item, "version");
        bool exact = query.Version is not null && version is not null &&
            version.Equals(query.Version.Value, StringComparison.OrdinalIgnoreCase);
        string citation = String(item, "citation") ?? String(item, "url") ??
            $"mcp:{route.Connection}/{route.Tool}";
        decimal confidence = Decimal(item, "confidence") ?? (exact ? 0.8m : 0.6m);
        output.Add(new(
            new($"mcp:{route.Connection}/{route.Tool}"),
            DocumentationSourceClass.Mcp,
            String(item, "title") ?? $"{route.Connection}/{route.Tool}",
            content,
            version is null ? null : new(version),
            new(citation),
            retrievedAt,
            DocumentationFileSearch.Sha256(content),
            exact,
            Boolean(item, "stale") ?? false,
            Math.Clamp(confidence, 0m, 1m)));
    }

    private static string? String(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Boolean(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static decimal? Decimal(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) &&
        value.TryGetDecimal(out decimal result) ? result : null;
}

internal sealed class HttpDocumentationSource(
    HttpClient httpClient,
    IResearchSettingsStore settingsStore,
    TimeProvider timeProvider) : IDocumentationSource
{
    public DocumentationSourceId Id { get; } = new("configured-web-documentation");

    public DocumentationSourceClass SourceClass => DocumentationSourceClass.Web;

    public async ValueTask<DocumentationSourceResult> SearchAsync(
        DocumentationSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        ResearchSourceSettings settings = await settingsStore.GetAsync(cancellationToken);
        List<DocumentationSourceMatch> matches = [];
        List<string> failures = [];
        foreach (DocumentationWebEndpoint endpoint in settings.WebEndpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (endpoint.Value.Scheme != Uri.UriSchemeHttps && !endpoint.Value.IsLoopback)
            {
                failures.Add($"{endpoint.Value.Host}: insecure endpoint");
                continue;
            }
            try
            {
                Uri request = BuildRequest(endpoint.Value, query);
                using HttpResponseMessage response = await httpClient.GetAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(stream,
                    new JsonDocumentOptions { MaxDepth = 48 }, cancellationToken);
                matches.AddRange(Parse(document.RootElement, endpoint, query,
                    timeProvider.GetUtcNow()));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
            {
                failures.Add($"{endpoint.Value.Host}: {exception.Message}");
            }
        }
        DocumentationSourceMatch[] ranked = DocumentationFileSearch.Rank(
            matches, query.MaximumResults, query.MaximumCharacters);
        return new(
            Id,
            SourceClass,
            ranked,
            ranked.Length > 0,
            failures.Count > 0 ? "web_documentation_incomplete" :
                ranked.Length == 0 ? "web_documentation_no_match" : null,
            failures.Count > 0 ? string.Join("; ", failures) :
                ranked.Length == 0 ? "No configured web source returned a match." : null);
    }

    private static Uri BuildRequest(Uri endpoint, DocumentationSourceQuery query)
    {
        string search = string.Join(' ', new[]
        {
            query.Library.Value,
            query.Version?.Value,
            query.Query.Value,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        UriBuilder builder = new(endpoint);
        string prefix = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : builder.Query.TrimStart('?') + "&";
        builder.Query = prefix + $"search={Uri.EscapeDataString(search)}&locale=en-us&$top={query.MaximumResults}";
        return builder.Uri;
    }

    private static IReadOnlyList<DocumentationSourceMatch> Parse(
        JsonElement root,
        DocumentationWebEndpoint endpoint,
        DocumentationSourceQuery query,
        DateTimeOffset retrievedAt)
    {
        JsonElement values = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("results", out JsonElement results) ? results : root;
        if (values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        List<DocumentationSourceMatch> output = [];
        foreach (JsonElement item in values.EnumerateArray())
        {
            string? content = String(item, "content") ?? String(item, "description") ??
                String(item, "summary");
            string? citation = String(item, "url") ?? String(item, "citation");
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(citation) ||
                !Uri.TryCreate(citation, UriKind.Absolute, out Uri? citationUri) ||
                citationUri.Scheme != Uri.UriSchemeHttp && citationUri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }
            string? version = String(item, "version");
            bool exact = query.Version is not null && version is not null &&
                version.Equals(query.Version.Value, StringComparison.OrdinalIgnoreCase);
            output.Add(new(
                new($"web:{endpoint.Value.Host}"),
                DocumentationSourceClass.Web,
                String(item, "title") ?? citationUri.Host,
                content,
                version is null ? null : new(version),
                new(citationUri.AbsoluteUri),
                retrievedAt,
                DocumentationFileSearch.Sha256(content),
                exact,
                IsStale: false,
                exact ? 0.75m : 0.55m));
        }
        return output;
    }

    private static string? String(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
