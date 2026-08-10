using System.Net;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Models;
using Harness.DataAccess.Models.OpenRouter;
using Harness.DataAccess.Secrets;

namespace Harness.DataAccess.Tests.Models;

public sealed class OpenRouterModelProviderTests
{
    [Fact]
    public async Task Discovers_chat_and_embedding_models_with_pricing()
    {
        List<string> paths = [];
        using HttpClient httpClient = CreateClient((request, _) =>
        {
            Assert.Equal("Bearer test-key", request.Headers.Authorization?.ToString());
            paths.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri.AbsolutePath == "/api/v1/embeddings/models"
                ? JsonResponse(ModelsJson("openai/text-embedding-3-small", "embeddings"))
                : JsonResponse(ModelsJson("openai/gpt-5-mini", "text"));
        });
        OpenRouterModelProvider provider = CreateProvider(
            httpClient,
            new StubRemoteCostStore(),
            providerName: "Cloud");

        ModelCatalog catalog = await provider.GetModelsAsync();

        Assert.Null(catalog.Error);
        Assert.Equal(2, catalog.Models.Count);
        Assert.Contains("/api/v1/models?output_modalities=text", paths);
        Assert.Contains("/api/v1/embeddings/models", paths);
        ModelDescriptor chat = Assert.Single(catalog.Models, model => model.Id == "openai/gpt-5-mini");
        ModelDescriptor embedding = Assert.Single(
            catalog.Models,
            model => model.Id == "openai/text-embedding-3-small");
        Assert.Equal("Cloud", chat.Provider);
        Assert.Equal(128_000, chat.ContextLength);
        Assert.Equal(0.000001m, chat.Pricing?.InputUsdPerToken);
        Assert.Contains("tools", chat.Capabilities);
        Assert.Equal([ModelPurpose.Chat], chat.Purposes);
        Assert.Equal([ModelPurpose.Embedding], embedding.Purposes);
    }

    [Fact]
    public async Task Streams_with_strict_privacy_and_reconciles_returned_cost()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(ModelsJson("openai/gpt-5-mini", "text"));
            }

            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "data: {\"choices\":[{\"delta\":{\"reasoning\":\"check\"}}]}\n\n" +
                ": OPENROUTER PROCESSING\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]," +
                "\"usage\":{\"prompt_tokens\":6,\"completion_tokens\":2,\"cost\":0.000007}}\n\n" +
                "data: [DONE]\n\n",
                "text/event-stream");
        });
        StubRemoteCostStore costs = new();
        OpenRouterModelProvider provider = CreateProvider(httpClient, costs);

        List<ChatStreamEvent> events = [];
        await foreach (ChatStreamEvent item in provider.StreamChatAsync(new(
                           "openai/gpt-5-mini",
                           [new(ChatRole.User, "hi")],
                           new(
                               "goal-1",
                               ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention,
                               RemoteModelRole.Lead))))
        {
            events.Add(item);
        }

        Assert.Equal("check", events[0].Thinking);
        Assert.Equal("hello", events[1].Content);
        Assert.True(events[2].Done);
        Assert.Equal(new MicroUsd(7), events[2].Usage.Cost);
        Assert.Equal(new MicroUsd(7), costs.Request?.EstimatedCost);
        Assert.Equal(RemoteModelRole.Lead, costs.Request?.Role);
        Assert.Equal(new MicroUsd(7), costs.ReconciledCost);
        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.False(body.RootElement.TryGetProperty("max_tokens", out _));
        JsonElement routing = body.RootElement.GetProperty("provider");
        Assert.Equal("deny", routing.GetProperty("data_collection").GetString());
        Assert.True(routing.GetProperty("zdr").GetBoolean());
    }

    [Fact]
    public async Task Sends_exact_visual_evidence_as_openrouter_multimodal_content()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(ModelsJson("openai/gpt-5-mini", "text"));
            }
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]," +
                "\"usage\":{\"cost\":0.000001}}\n\ndata: [DONE]\n\n",
                "text/event-stream");
        });
        OpenRouterModelProvider provider = CreateProvider(httpClient, new StubRemoteCostStore());

        _ = await CollectAsync(provider.StreamChatAsync(new(
            "openai/gpt-5-mini",
            [new(ChatRole.User, "Inspect exact frame", Image: new(new("image/png"), new("AQID")))],
            new("goal-image", ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention,
                RemoteModelRole.Reviewer))));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        JsonElement content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("Inspect exact frame", content[0].GetProperty("text").GetString());
        Assert.Equal("data:image/png;base64,AQID", content[1].GetProperty("image_url")
            .GetProperty("url").GetString());
    }

    [Fact]
    public async Task Capped_goal_derives_provider_output_boundary_from_remaining_money()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(ModelsJson("openai/gpt-5-mini", "text"));
            }

            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]," +
                "\"usage\":{\"prompt_tokens\":6,\"completion_tokens\":2,\"cost\":0.000007}}\n\n" +
                "data: [DONE]\n\n",
                "text/event-stream");
        });
        StubRemoteCostStore costs = new()
        {
            Ledger = new(
                "goal-capped",
                new(21),
                new(0),
                new(0),
                new(21),
                new(0),
                []),
        };
        OpenRouterModelProvider provider = CreateProvider(httpClient, costs);

        List<ChatStreamEvent> events = await CollectAsync(provider.StreamChatAsync(new(
            "openai/gpt-5-mini",
            [new(ChatRole.User, "hi")],
            new("goal-capped", ProviderPrivacyPolicy.Normal, RemoteModelRole.Lead))));

        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal(7, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(new MicroUsd(21), costs.Request?.EstimatedCost);
        Assert.Equal("remote_cost_cap_exceeded", events[^1].Error?.Code);
    }

    [Fact]
    public async Task Embeds_and_reconciles_cost()
    {
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(ModelsJson("openai/text-embedding-3-small", "embeddings"));
            }

            using JsonDocument body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.False(body.RootElement.TryGetProperty("provider", out _));
            return JsonResponse("""
                    {
                      "data": [{"index": 0, "embedding": [0.25, -0.5]}],
                      "usage": {"prompt_tokens": 1, "total_tokens": 1, "cost": 0.000003}
                    }
                    """);
        });
        StubRemoteCostStore costs = new();
        OpenRouterModelProvider provider = CreateProvider(httpClient, costs);

        EmbeddingResult result = await provider.EmbedAsync(new(
            "openai/text-embedding-3-small",
            ["x"],
            RemoteScope: new("goal-1", ProviderPrivacyPolicy.Normal)));

        Assert.Null(result.Error);
        Assert.Equal([0.25f, -0.5f], Assert.Single(result.Embeddings));
        Assert.Equal(new MicroUsd(3), result.Usage.Cost);
        Assert.Equal(new MicroUsd(3), costs.ReconciledCost);
    }

    [Fact]
    public async Task Maps_streamed_tool_calls_and_sends_typed_definitions()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(ModelsJson("openai/gpt-5-mini", "text"));
            }

            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{" +
                "\"index\":0,\"id\":\"call-9\",\"function\":{" +
                "\"name\":\"read_file\",\"arguments\":\"{\\\"relativePath\\\":\\\"\"}}]}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{" +
                "\"index\":0,\"function\":{\"arguments\":\"README.md\\\"}\"}}]}," +
                "\"finish_reason\":\"tool_calls\"}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{" +
                "\"prompt_tokens\":8,\"completion_tokens\":3,\"cost\":0.000004}}\n\n" +
                "data: [DONE]\n\n",
                "text/event-stream");
        });
        StubRemoteCostStore costs = new();
        OpenRouterModelProvider provider = CreateProvider(httpClient, costs);

        List<ChatStreamEvent> events = await CollectAsync(provider.StreamChatAsync(new(
            "openai/gpt-5-mini",
            [new(ChatRole.User, "inspect")],
            new("goal-tools", ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention,
                RemoteModelRole.Reviewer),
            [new(new("read_file"), new("Read one file."),
                new("{\"type\":\"object\",\"properties\":{}}"))])));

        ChatToolCall call = Assert.Single(events.SelectMany(item => item.ToolCalls ?? []));
        Assert.Equal("call-9", call.Id.Value);
        Assert.Equal("read_file", call.Name.Value);
        Assert.Equal("{\"relativePath\":\"README.md\"}", call.Arguments.Value);
        Assert.Equal(RemoteModelRole.Reviewer, costs.Request?.Role);
        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal("read_file", body.RootElement.GetProperty("tools")[0]
            .GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Preserves_reasoning_details_and_maps_explicit_effort()
    {
        string? requestJson = null;
        using HttpClient httpClient = CreateClient(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(ModelsJson("deepseek/deepseek-v4-flash", "text"));
            }

            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "data: {\"choices\":[{\"delta\":{\"reasoning\":\"check\"," +
                "\"reasoning_details\":[{\"type\":\"reasoning.text\",\"text\":\"check\"}]}," +
                "\"finish_reason\":\"stop\"}],\"usage\":{\"cost\":0.000001}}\n\n" +
                "data: [DONE]\n\n",
                "text/event-stream");
        });
        OpenRouterModelProvider provider = CreateProvider(httpClient, new StubRemoteCostStore());
        const string details = "[{\"type\":\"reasoning.text\",\"text\":\"prior\"}]";

        List<ChatStreamEvent> events = await CollectAsync(provider.StreamChatAsync(new(
            "deepseek/deepseek-v4-flash",
            [new(
                ChatRole.Assistant,
                string.Empty,
                Reasoning: new(new("prior"), new(details)))],
            new("goal-reasoning", ProviderPrivacyPolicy.Normal, RemoteModelRole.Lead),
            ReasoningEffort: ModelReasoningEffort.Low)));

        ChatStreamEvent completed = Assert.Single(events, item => item.Done);
        Assert.Equal("check", completed.Thinking);
        Assert.Contains("reasoning.text", completed.ReasoningDetails?.Value,
            StringComparison.Ordinal);
        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal("low", body.RootElement.GetProperty("reasoning")
            .GetProperty("effort").GetString());
        Assert.Equal("prior", body.RootElement.GetProperty("messages")[0]
            .GetProperty("reasoning").GetString());
        Assert.Equal("prior", body.RootElement.GetProperty("messages")[0]
            .GetProperty("reasoning_details")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Rejects_chat_without_goal_scope_before_transport_or_reservation()
    {
        int requests = 0;
        using HttpClient httpClient = CreateClient((_, _) =>
        {
            requests++;
            return JsonResponse("{}");
        });
        StubRemoteCostStore costs = new();
        OpenRouterModelProvider provider = CreateProvider(httpClient, costs);

        ChatStreamEvent result = Assert.Single(await CollectAsync(provider.StreamChatAsync(
            new("model", [new(ChatRole.User, "hello")]))));

        Assert.Equal("remote_scope_required", result.Error?.Code);
        Assert.Equal(0, requests);
        Assert.Null(costs.Request);
    }

    private static OpenRouterModelProvider CreateProvider(
        HttpClient httpClient,
        IRemoteCostStore costStore,
        string providerName = "OpenRouter") =>
        new(
            httpClient,
            providerName,
            new StubSecretStore(),
            new("openrouter-api-key", "OPENROUTER_API_KEY"),
            costStore);

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> values = [];
        await foreach (T value in source)
        {
            values.Add(value);
        }

        return values;
    }

    private static string ModelsJson(string id, string outputModality) => $$"""
        {
          "data": [{
            "id": "{{id}}",
            "context_length": 128000,
            "architecture": {"tokenizer": "GPT", "output_modalities": ["{{outputModality}}"]},
            "supported_parameters": ["tools"],
            "pricing": {"prompt": "0.000001", "completion": "0.000002", "request": "0.000001"}
          }]
        }
        """;

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://openrouter.test/"),
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

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class StubSecretStore : ISecretStore
    {
        public ValueTask<string?> GetAsync(
            SecretReference reference,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>("test-key");

        public ValueTask SetAsync(
            SecretReference reference,
            string value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubRemoteCostStore : IRemoteCostStore
    {
        public RemoteCostLedger? Ledger { get; init; }

        public RemoteCostReservationRequest? Request { get; private set; }

        public MicroUsd? ReconciledCost { get; private set; }

        public ValueTask<RemoteCostLedger?> GetLedgerAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Ledger);

        public ValueTask<RemoteCostReservationResult> ReserveAsync(
            RemoteCostReservationRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(new RemoteCostReservationResult(
                new("reservation-1", request.EstimatedCost),
                Failure: null));
        }

        public ValueTask ReconcileAsync(
            string reservationId,
            MicroUsd actualCost,
            CancellationToken cancellationToken = default)
        {
            ReconciledCost = actualCost;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(
            string reservationId,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
