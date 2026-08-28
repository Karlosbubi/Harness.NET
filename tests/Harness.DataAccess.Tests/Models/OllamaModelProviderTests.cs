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
                      "quantization_level": "Q4_K_M",
                      "context_length": 131072
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
        Assert.Equal(131072, model.ContextLength);
        Assert.Equal(["completion", "tools", "thinking"], model.Capabilities);
        Assert.Equal([ModelPurpose.Chat], model.Purposes);
    }

    [Fact]
    public async Task Streams_chat_content_thinking_and_usage()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient((request, _) =>
        {
            Assert.Equal("/api/chat", request.RequestUri?.AbsolutePath);
            requestJson = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
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
        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal(4098, body.RootElement.GetProperty("options")
            .GetProperty("num_ctx").GetInt32());
    }

    [Fact]
    public async Task Caps_agent_context_by_configuration_and_model_advertisement()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("""
                    {
                      "models": [{
                        "model": "small-context",
                        "details": { "context_length": 6000 },
                        "capabilities": ["completion", "tools"]
                      }]
                    }
                    """);
            }

            requestJson = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(
                "{\"message\":{\"content\":\"ok\"},\"done\":true}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient, new(7_000));

        _ = await provider.GetModelsAsync();
        _ = await CollectAsync(provider.StreamChatAsync(new(
            "small-context",
            [new(ChatRole.User, new string('x', 5_000))])));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal(6000, body.RootElement.GetProperty("options")
            .GetProperty("num_ctx").GetInt32());
    }

    [Fact]
    public async Task Sends_exact_visual_evidence_as_an_ollama_image()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("{\"message\":{\"content\":\"seen\"},\"done\":true}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);

        _ = await CollectAsync(provider.StreamChatAsync(new(
            "vision-model",
            [new(ChatRole.User, "Inspect exact frame", Image: new(new("image/png"), new("AQID")))])));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        JsonElement message = body.RootElement.GetProperty("messages")[0];
        Assert.Equal("Inspect exact frame", message.GetProperty("content").GetString());
        Assert.Equal("AQID", message.GetProperty("images")[0].GetString());
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
        Assert.False(body.RootElement.TryGetProperty("think", out _));
        Assert.Equal(0, body.RootElement.GetProperty("options")
            .GetProperty("temperature").GetDouble());
        Assert.Equal("{\"content\":\"prior\"}", body.RootElement.GetProperty("messages")[1]
            .GetProperty("content").GetString());
    }

    [Fact]
    public async Task Disables_thinking_for_a_deterministic_tool_free_request()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("{\"message\":{\"content\":\"source\"},\"done\":true}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);

        _ = await CollectAsync(provider.StreamChatAsync(new(
            "thinking-model",
            [new(ChatRole.User, "return deterministic source")],
            ReasoningEffort: ModelReasoningEffort.None)));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.False(body.RootElement.GetProperty("think").GetBoolean());
    }

    [Fact]
    public async Task Maps_reasoning_effort_without_disabling_tools_by_default()
    {
        List<string> requests = [];
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return JsonResponse("{\"message\":{\"content\":\"ok\"},\"done\":true}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);
        ChatToolDefinition tool = new(
            new("read_file"),
            new("Read a file."),
            new("{\"type\":\"object\"}"));

        _ = await CollectAsync(provider.StreamChatAsync(new(
            "reasoning-model",
            [new(ChatRole.User, "inspect")],
            Tools: [tool])));
        _ = await CollectAsync(provider.StreamChatAsync(new(
            "reasoning-model",
            [new(ChatRole.User, "inspect")],
            Tools: [tool],
            ReasoningEffort: ModelReasoningEffort.Low)));

        using JsonDocument providerDefault = JsonDocument.Parse(requests[0]);
        using JsonDocument low = JsonDocument.Parse(requests[1]);
        Assert.False(providerDefault.RootElement.TryGetProperty("think", out _));
        Assert.Equal("low", low.RootElement.GetProperty("think").GetString());
    }

    [Fact]
    public async Task Roundtrips_assistant_thinking_and_named_tool_result()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("{\"message\":{\"content\":\"done\"},\"done\":true}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);

        _ = await CollectAsync(provider.StreamChatAsync(new(
            "reasoning-model",
            [
                new(ChatRole.User, "inspect"),
                new(
                    ChatRole.Assistant,
                    string.Empty,
                    [new(new("call-1"), new("read_file"), new("{}"))],
                    Reasoning: new(new("I should inspect first."))),
                new(
                    ChatRole.Tool,
                    string.Empty,
                    ToolResult: new(
                        new("call-1"),
                        new("{\"content\":\"source\"}"),
                        new("read_file"))),
            ])));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal("I should inspect first.", body.RootElement.GetProperty("messages")[1]
            .GetProperty("thinking").GetString());
        Assert.Equal("read_file", body.RootElement.GetProperty("messages")[2]
            .GetProperty("tool_name").GetString());
    }

    [Fact]
    public async Task Requests_native_json_mode_for_structured_role_output()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "{\"message\":{\"content\":\"{}\"},\"done\":true}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);

        _ = await CollectAsync(provider.StreamChatAsync(new(
            "model",
            [new(ChatRole.User, "return json")],
            ResponseFormat: ChatResponseFormat.Json)));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal("json", body.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Maps_an_explicit_sampling_temperature()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "{\"message\":{\"content\":\"ok\"},\"done\":true}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);

        _ = await CollectAsync(provider.StreamChatAsync(new(
            "model",
            [new(ChatRole.User, "deterministic")],
            Temperature: 0)));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal(0, body.RootElement.GetProperty("options")
            .GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task Requests_native_json_schema_for_exact_structured_output()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "{\"message\":{\"content\":\"{}\"},\"done\":true}\n",
                "application/x-ndjson");
        });
        OllamaModelProvider provider = new(httpClient);

        _ = await CollectAsync(provider.StreamChatAsync(new(
            "model",
            [new(ChatRole.User, "return an object")],
            ResponseFormat: ChatResponseFormat.Json,
            ResponseSchema: new("{\"type\":\"object\",\"required\":[\"value\"]}"))));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        JsonElement format = body.RootElement.GetProperty("format");
        Assert.Equal("object", format.GetProperty("type").GetString());
        Assert.Equal("value", format.GetProperty("required")[0].GetString());
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
