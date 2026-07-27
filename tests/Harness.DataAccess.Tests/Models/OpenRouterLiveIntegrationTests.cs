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
}
