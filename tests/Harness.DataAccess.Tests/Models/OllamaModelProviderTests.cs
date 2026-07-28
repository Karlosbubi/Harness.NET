using System.Net;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Models;
using Harness.DataAccess.Models.Ollama;

namespace Harness.DataAccess.Tests.Models;

public sealed class OllamaModelProviderTests
{
    [Fact]
    public async Task Maps_model_catalog()
    {
        using HttpClient httpClient = CreateClient((request, _) =>
        {
            Assert.Equal("/api/tags", request.RequestUri?.AbsolutePath);
            return JsonResponse("""
                {
                  "models": [{
                    "name": "gemma4:latest",
                    "model": "gemma4:latest",
                    "details": {
                      "family": "gemma4",
                      "parameter_size": "8B",
                      "quantization_level": "Q4_K_M"
                    },
                    "capabilities": ["completion", "tools", "thinking"]
                  }]
                }
                """);
        });
        OllamaModelProvider provider = new(httpClient);

        ModelCatalog catalog = await provider.GetModelsAsync();

        ModelDescriptor model = Assert.Single(catalog.Models);
        Assert.Null(catalog.Error);
        Assert.Equal("gemma4:latest", model.Id);
        Assert.Equal("Ollama", model.Provider);
        Assert.Equal("8B", model.ParameterSize);
        Assert.Equal(["completion", "tools", "thinking"], model.Capabilities);
        Assert.Equal([ModelPurpose.Chat], model.Purposes);
    }

    [Fact]
    public async Task Streams_chat_content_thinking_and_usage()
    {
        using HttpClient httpClient = CreateClient((request, _) =>
        {
            Assert.Equal("/api/chat", request.RequestUri?.AbsolutePath);
            return JsonResponse(
                "{\"message\":{\"thinking\":\"checking\"},\"done\":false}\n" +
                "{\"message\":{\"content\":\"hello\"},\"done\":false}\n" +
                "{\"message\":{\"content\":\"\"},\"done\":true," +
                "\"done_reason\":\"stop\",\"prompt_eval_count\":7,\"eval_count\":2}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);

        List<ChatStreamEvent> events = [];
        await foreach (ChatStreamEvent item in provider.StreamChatAsync(
                           new("gemma4:latest", [new(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.Equal(3, events.Count);
        Assert.Equal("checking", events[0].Thinking);
        Assert.Equal("hello", events[1].Content);
        Assert.True(events[2].Done);
        Assert.Equal(new ProviderUsage(7, 2), events[2].Usage);
    }

    [Fact]
    public async Task Maps_mid_stream_errors()
    {
        using HttpClient httpClient = CreateClient((_, _) => JsonResponse(
            "{\"message\":{\"content\":\"partial\"},\"done\":false}\n" +
            "{\"error\":\"model runner stopped\"}\n",
            "application/x-ndjson"));
        OllamaModelProvider provider = new(httpClient);

        List<ChatStreamEvent> events = [];
        await foreach (ChatStreamEvent item in provider.StreamChatAsync(
                           new("model", [new(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.Equal("partial", events[0].Content);
        Assert.Equal("stream_error", events[1].Error?.Code);
        Assert.Equal("model runner stopped", events[1].Error?.Message);
    }

    [Fact]
    public async Task Maps_typed_tools_calls_and_results()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "{\"message\":{\"tool_calls\":[{\"function\":{" +
                "\"name\":\"read_file\",\"arguments\":{\"relativePath\":\"README.md\"}}}]}," +
                "\"done\":true,\"done_reason\":\"stop\"}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);

        ChatStreamEvent result = Assert.Single(await CollectAsync(provider.StreamChatAsync(new(
            "tool-model",
            [
                new(ChatRole.User, "inspect"),
                new(ChatRole.Tool, string.Empty, ToolResult: new(
                    new("previous-call"), new("{\"content\":\"prior\"}"))),
            ],
            Tools:
            [
                new(new("read_file"), new("Read one file."),
                    new("{\"type\":\"object\",\"properties\":{}}")),
            ]))));

        ChatToolCall call = Assert.Single(result.ToolCalls!);
        Assert.Equal("read_file", call.Name.Value);
        Assert.Contains("README.md", call.Arguments.Value, StringComparison.Ordinal);
        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal("read_file", body.RootElement.GetProperty("tools")[0]
            .GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{\"content\":\"prior\"}", body.RootElement.GetProperty("messages")[1]
            .GetProperty("content").GetString());
    }

    [Fact]
    public async Task Maps_embeddings_and_token_usage()
    {
        using HttpClient httpClient = CreateClient((request, _) =>
        {
            Assert.Equal("/api/embed", request.RequestUri?.AbsolutePath);
            return JsonResponse("""
                {
                  "embeddings": [[0.25, -0.5, 0.75]],
                  "prompt_eval_count": 4
                }
                """);
        });
        OllamaModelProvider provider = new(httpClient);

        EmbeddingResult result = await provider.EmbedAsync(
            new("embeddinggemma", ["sample"]));

        Assert.Null(result.Error);
        Assert.Equal([0.25f, -0.5f, 0.75f], Assert.Single(result.Embeddings));
        Assert.Equal(4, result.Usage.InputTokens);
    }

    [Fact]
    public async Task Maps_http_failures_to_provider_errors()
    {
        using HttpClient httpClient = CreateClient((_, _) => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("{\"error\":\"model is loading\"}", Encoding.UTF8, "application/json"),
        });
        OllamaModelProvider provider = new(httpClient);

        ModelCatalog result = await provider.GetModelsAsync();

        Assert.Empty(result.Models);
        Assert.Equal("http_503", result.Error?.Code);
        Assert.True(result.Error?.IsTransient);
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        using HttpClient httpClient = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });
        OllamaModelProvider provider = new(httpClient);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await provider.GetModelsAsync(cancellation.Token));
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("http://ollama.test/"),
        };

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        CreateClient((request, cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken)));

    private static HttpResponseMessage JsonResponse(
        string json,
        string mediaType = "application/json") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, mediaType),
    };

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> values = [];
        await foreach (T value in source)
        {
            values.Add(value);
        }

        return values;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
