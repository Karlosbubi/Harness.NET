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

        List<ChatStreamEvent> events = [];
        await foreach (ChatStreamEvent item in provider.StreamChatAsync(new(
                           expectedModel,
                           [new("user", "Reply with exactly HARNESS_PROVIDER_OK")])) )
        {
            events.Add(item);
        }

        Assert.DoesNotContain(events, item => item.Error is not null);
        Assert.True(events[^1].Done);
        Assert.True(events[^1].Usage.InputTokens > 0);
        Assert.True(events[^1].Usage.OutputTokens > 0);
        Assert.Contains(
            "HARNESS_PROVIDER_OK",
            string.Concat(events.Select(item => item.Content)),
            StringComparison.OrdinalIgnoreCase);
    }
}
