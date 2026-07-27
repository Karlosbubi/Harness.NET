using Harness.DataAccess.Models;
using Harness.DataAccess.Models.OpenRouter;
using Harness.DataAccess.Secrets;

namespace Harness.DataAccess.Tests.Models;

public sealed class OpenRouterLiveIntegrationTests
{
    [Fact]
    [Trait("Category", "OpenRouterLiveIntegration")]
    public async Task Discovers_chat_and_embedding_catalogs_without_inference_spend()
    {
        string? apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (apiKey is null)
        {
            return;
        }

        using HttpClient httpClient = new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            BaseAddress = new Uri("https://openrouter.ai", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30),
        };
        OpenRouterModelProvider provider = new(
            httpClient,
            new EnvironmentSecretStore(apiKey),
            new("openrouter-api-key"),
            new RejectingCostStore());

        ModelCatalog catalog = await provider.GetModelsAsync();

        Assert.Null(catalog.Error);
        Assert.NotEmpty(catalog.Models);
        Assert.Contains(catalog.Models, model => model.Capabilities.Contains("text"));
        Assert.Contains(catalog.Models, model => model.Capabilities.Contains("embeddings"));
        Assert.All(catalog.Models, model => Assert.NotNull(model.Pricing));
    }

    [Fact]
    [Trait("Category", "OpenRouterPaidLiveIntegration")]
    public async Task Embeds_one_short_input_under_a_five_microdollar_ceiling()
    {
        string? apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (apiKey is null ||
            Environment.GetEnvironmentVariable("HARNESS_RUN_OPENROUTER_PAID_TESTS") != "1")
        {
            return;
        }

        using HttpClient httpClient = new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            BaseAddress = new Uri("https://openrouter.ai", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30),
        };
        BoundedCostStore costs = new(new(5));
        OpenRouterModelProvider provider = new(
            httpClient,
            new EnvironmentSecretStore(apiKey),
            new("openrouter-api-key"),
            costs);

        EmbeddingResult result = await provider.EmbedAsync(new(
            "openai/text-embedding-3-small",
            ["Harness.NET retrieval smoke test."],
            Dimensions: 1536,
            RemoteScope: new("live-test-goal", ProviderPrivacyPolicy.Normal)));

        Assert.Null(result.Error);
        Assert.Equal(1536, Assert.Single(result.Embeddings).Count);
        Assert.InRange(Assert.IsType<MicroUsd>(costs.Reserved).Value, 0, 5);
        Assert.InRange(Assert.IsType<MicroUsd>(costs.Reconciled).Value, 0, 5);
    }

    private sealed class EnvironmentSecretStore(string apiKey) : ISecretStore
    {
        public ValueTask<string?> GetAsync(
            SecretReference reference,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(apiKey);

        public ValueTask SetAsync(
            SecretReference reference,
            string value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RejectingCostStore : IRemoteCostStore
    {
        public ValueTask<RemoteCostLedger?> GetLedgerAsync(
            string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RemoteCostReservationResult> ReserveAsync(
            RemoteCostReservationRequest request,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(
                "Catalog discovery must not reserve or spend inference budget.");

        public ValueTask ReconcileAsync(
            string reservationId,
            MicroUsd actualCost,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ReleaseAsync(
            string reservationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BoundedCostStore(MicroUsd ceiling) : IRemoteCostStore
    {
        public MicroUsd? Reserved { get; private set; }

        public MicroUsd? Reconciled { get; private set; }

        public ValueTask<RemoteCostLedger?> GetLedgerAsync(
            string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RemoteCostReservationResult> ReserveAsync(
            RemoteCostReservationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.EstimatedCost.Value > ceiling.Value)
            {
                return ValueTask.FromResult(new RemoteCostReservationResult(
                    Reservation: null,
                    RemoteCostReservationFailure.CostCapExceeded));
            }

            Reserved = request.EstimatedCost;
            return ValueTask.FromResult(new RemoteCostReservationResult(
                new("live-reservation", request.EstimatedCost),
                Failure: null));
        }

        public ValueTask ReconcileAsync(
            string reservationId,
            MicroUsd actualCost,
            CancellationToken cancellationToken = default)
        {
            Reconciled = actualCost;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(
            string reservationId,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
