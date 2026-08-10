using Harness.DataAccess.Models;
using Harness.DataAccess.Models.Ollama;

namespace Harness.DataAccess.Tests.Models;

public sealed class OllamaLiveIntegrationTests
{
    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Discovers_and_streams_from_configured_server()
    {
        string? configuredEndpoint = Environment.GetEnvironmentVariable(
            "HARNESS_OLLAMA_INTEGRATION_ENDPOINT");
        if (configuredEndpoint is null)
        {
            return;
        }

        string expectedModel = Environment.GetEnvironmentVariable(
            "HARNESS_OLLAMA_INTEGRATION_MODEL") ?? "gemma4:latest";
        using HttpClient httpClient = new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            BaseAddress = new Uri(configuredEndpoint, UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(3),
        };
        OllamaModelProvider provider = new(httpClient);

        ModelCatalog catalog = await provider.GetModelsAsync();
        ModelDescriptor model = Assert.Single(
            catalog.Models,
            available => available.Id == expectedModel);
        Assert.Null(catalog.Error);
        Assert.Contains("completion", model.Capabilities);

        List<ChatStreamEvent> events = await CollectAsync(provider.StreamChatAsync(new(
            expectedModel,
            [new(ChatRole.User, "Reply with exactly HARNESS_PROVIDER_OK")])));

        Assert.DoesNotContain(events, item => item.Error is not null);
        Assert.True(events[^1].Done);
        Assert.True(events[^1].Usage.InputTokens > 0);
        Assert.True(events[^1].Usage.OutputTokens > 0);
        Assert.Contains(
            "HARNESS_PROVIDER_OK",
            string.Concat(events.Select(item => item.Content)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "OllamaLiveIntegration")]
    public async Task Ornith_combines_reasoning_with_a_typed_tool_call()
    {
        if (Environment.GetEnvironmentVariable("HARNESS_RUN_OLLAMA_LIVE_TESTS") != "1")
        {
            return;
        }

        string endpoint = Environment.GetEnvironmentVariable("HARNESS_OLLAMA_ENDPOINT") ??
            "http://ollama.local.brunner.codes:11434";
        string model = Environment.GetEnvironmentVariable("HARNESS_OLLAMA_MODEL") ?? "ornith:9b";
        using HttpClient httpClient = new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            BaseAddress = new Uri(endpoint, UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(3),
        };
        OllamaModelProvider provider = new(httpClient);

        List<ChatStreamEvent> events = await CollectAsync(provider.StreamChatAsync(new(
            model,
            [new(ChatRole.User,
                "Call lookup_value exactly once with key alpha. Do not answer directly.")],
            Tools:
            [
                new(
                    new("lookup_value"),
                    new("Look up the deterministic value for a key."),
                    new("{\"type\":\"object\",\"required\":[\"key\"]," +
                        "\"properties\":{\"key\":{\"type\":\"string\"}}}")),
            ])));

        Assert.DoesNotContain(events, item => item.Error is not null);
        ChatToolCall call = Assert.Single(events.SelectMany(item => item.ToolCalls ?? []));
        Assert.Equal("lookup_value", call.Name.Value);
        Assert.Contains("alpha", call.Arguments.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events, item => !string.IsNullOrWhiteSpace(item.Thinking));
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> values = [];
        await foreach (T value in source)
        {
            values.Add(value);
        }

        return values;
    }
}
