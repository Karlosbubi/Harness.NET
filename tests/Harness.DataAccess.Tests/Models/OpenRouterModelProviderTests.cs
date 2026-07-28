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
                           [new("user", "hi")],
                           new(
                               "goal-1",
                               ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention,
                               RemoteModelRole.Lead),
                           new(2))))
        {
            events.Add(item);
        }

        Assert.Equal("check", events[0].Thinking);
        Assert.Equal("hello", events[1].Content);
        Assert.True(events[2].Done);
        Assert.Equal(new MicroUsd(7), events[2].Usage.Cost);
        Assert.Equal(new MicroUsd(11), costs.Request?.EstimatedCost);
        Assert.Equal(RemoteModelRole.Lead, costs.Request?.Role);
        Assert.Equal(new MicroUsd(7), costs.ReconciledCost);
        using JsonDocument body = JsonDocument.Parse(requestJson!);
        Assert.Equal(2, body.RootElement.GetProperty("max_tokens").GetInt32());
        JsonElement routing = body.RootElement.GetProperty("provider");
        Assert.Equal("deny", routing.GetProperty("data_collection").GetString());
        Assert.True(routing.GetProperty("zdr").GetBoolean());
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
            new("model", [new("user", "hello")], MaximumOutputTokens: new(10)))));

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
        public RemoteCostReservationRequest? Request { get; private set; }

        public MicroUsd? ReconciledCost { get; private set; }

        public ValueTask<RemoteCostLedger?> GetLedgerAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RemoteCostLedger?>(null);

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
