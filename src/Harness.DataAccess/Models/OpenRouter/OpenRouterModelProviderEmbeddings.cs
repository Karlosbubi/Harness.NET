using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Secrets;

namespace Harness.DataAccess.Models.OpenRouter;

internal sealed partial class OpenRouterModelProvider
{
    public async ValueTask<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        ProviderError? validationError = ValidateRemoteRequest(request.RemoteScope);
        if (validationError is not null)
        {
            return EmbeddingFailure(validationError);
        }

        string? apiKey = await secretStore.GetAsync(apiKeyReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return EmbeddingFailure(MissingCredential());
        }

        ModelPricingResult pricing = await GetPricingAsync(request.Model, apiKey, cancellationToken);
        if (pricing.Error is not null)
        {
            return EmbeddingFailure(pricing.Error);
        }

        MicroUsd estimate = EstimateEmbeddingCost(request, pricing.Pricing!);
        RemoteCostReservationResult reservationResult = await remoteCostStore.ReserveAsync(new(
            request.RemoteScope!.GoalId,
            providerName,
            request.Model,
            RemoteCostOperation.Embedding,
            estimate,
            request.RemoteScope.Role), cancellationToken);
        if (reservationResult.Reservation is null)
        {
            return EmbeddingFailure(ReservationError(reservationResult.Failure));
        }

        RemoteCostReservation reservation = reservationResult.Reservation;
        bool requestAccepted = false;
        MicroUsd? actualCost = null;
        try
        {
            using HttpRequestMessage message = CreateAuthorizedRequest(
                HttpMethod.Post,
                "api/v1/embeddings",
                apiKey);
            message.Content = JsonContent.Create(new OpenRouterEmbeddingRequestPayload
            {
                Model = request.Model,
                Input = request.Inputs,
                Dimensions = request.Dimensions,
                Provider = CreateRouting(request.RemoteScope.PrivacyPolicy),
            });

            HttpResponseMessage? response = null;
            ProviderError? transportError = null;
            try
            {
                response = await httpClient.SendAsync(message, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                transportError = TransportError(exception);
            }

            if (transportError is not null)
            {
                return EmbeddingFailure(transportError);
            }

            if (response is null)
            {
                return EmbeddingFailure(MissingResponse());
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    return EmbeddingFailure(await ReadErrorAsync(response, cancellationToken));
                }

                requestAccepted = true;
                OpenRouterEmbeddingResponse? payload;
                try
                {
                    payload = await response.Content.ReadFromJsonAsync<OpenRouterEmbeddingResponse>(
                        JsonOptions,
                        cancellationToken);
                }
                catch (JsonException exception)
                {
                    return EmbeddingFailure(InvalidResponse(exception));
                }

                if (payload?.Error is not null)
                {
                    return EmbeddingFailure(MapStreamError(payload.Error));
                }

                actualCost = ToMicroUsd(payload?.Usage?.Cost);
                IReadOnlyList<IReadOnlyList<float>> embeddings = payload?.Data
                    .OrderBy(item => item.Index)
                    .Select(item => (IReadOnlyList<float>)item.Embedding)
                    .ToArray() ?? [];
                return new(
                    embeddings,
                    new(
                        payload?.Usage?.PromptTokens ?? 0,
                        payload?.Usage?.CompletionTokens ?? 0,
                        actualCost),
                    Error: null);
            }
        }
        finally
        {
            if (requestAccepted)
            {
                await remoteCostStore.ReconcileAsync(
                    reservation.Id,
                    actualCost ?? reservation.EstimatedCost,
                    CancellationToken.None);
            }
            else
            {
                await remoteCostStore.ReleaseAsync(reservation.Id, CancellationToken.None);
            }
        }
    }

    private async ValueTask<ModelPricingResult> GetPricingAsync(
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        lock (pricingLock)
        {
            if (pricingByModel.TryGetValue(model, out ModelPricing? cached))
            {
                return new(cached, Error: null);
            }
        }

        (IReadOnlyList<ModelDescriptor> models, ProviderError? error) =
            await GetCatalogAsync(
                "api/v1/models?output_modalities=all",
                ModelPurpose.Chat,
                apiKey,
                cancellationToken);
        if (error is not null)
        {
            return new(null, error);
        }

        lock (pricingLock)
        {
            foreach (ModelDescriptor descriptor in models)
            {
                if (descriptor.Pricing is not null)
                {
                    pricingByModel[descriptor.Id] = descriptor.Pricing;
                }
                if (descriptor.ContextLength is > 0)
                {
                    contextLengthByModel[descriptor.Id] = descriptor.ContextLength.Value;
                }
            }

            return pricingByModel.TryGetValue(model, out ModelPricing? found)
                ? new(found, Error: null)
                : new(null, new(
                    "pricing_unavailable",
                    $"OpenRouter did not publish pricing for model '{model}'.",
                    IsTransient: false));
        }
    }

    private async ValueTask<(IReadOnlyList<ModelDescriptor>, ProviderError?)> GetCatalogAsync(
        string path,
        ModelPurpose purpose,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateAuthorizedRequest(HttpMethod.Get, path, apiKey);
        HttpResponseMessage? response = null;
        ProviderError? transportError = null;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            transportError = TransportError(exception);
        }

        if (transportError is not null)
        {
            return ([], transportError);
        }

        if (response is null)
        {
            return ([], MissingResponse());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return ([], await ReadErrorAsync(response, cancellationToken));
            }

            try
            {
                OpenRouterModelsResponse? payload = await response.Content
                    .ReadFromJsonAsync<OpenRouterModelsResponse>(JsonOptions, cancellationToken);
                ModelDescriptor[] models = payload?.Data
                    .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                    .Select(model => MapModel(model, purpose))
                    .ToArray() ?? [];
                return (models, null);
            }
            catch (JsonException exception)
            {
                return ([], InvalidResponse(exception));
            }
        }
    }

    private ModelDescriptor MapModel(OpenRouterModel model, ModelPurpose purpose)
    {
        string[] capabilities = model.SupportedParameters
            .Concat(model.Architecture?.OutputModalities ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new(
            model.Id!,
            providerName,
            model.Architecture?.Tokenizer,
            ParameterSize: null,
            Quantization: null,
            capabilities,
            model.ContextLength,
            ParsePricing(model.Pricing),
            [purpose]);
    }

    private static ModelDescriptor Merge(ModelDescriptor first, ModelDescriptor second) =>
        first with
        {
            Capabilities = first.Capabilities
                .Concat(second.Capabilities)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ContextLength = first.ContextLength ?? second.ContextLength,
            Pricing = first.Pricing ?? second.Pricing,
            Purposes = (first.Purposes ?? [])
                .Concat(second.Purposes ?? [])
                .Distinct()
                .ToArray(),
        };

    private static ModelPricing? ParsePricing(OpenRouterPricing? pricing) =>
        pricing is null ||
        !TryParsePrice(pricing.Prompt, out decimal input) ||
        !TryParsePrice(pricing.Completion, out decimal output) ||
        !TryParsePrice(pricing.Request, out decimal request)
            ? null
            : new(input, output, request);

    private static bool TryParsePrice(string? value, out decimal result) =>
        decimal.TryParse(value ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
        result >= 0;

    private async ValueTask<ChatCostBoundary> ResolveChatCostBoundaryAsync(
        ChatRequest request,
        ModelPricing pricing,
        CancellationToken cancellationToken)
    {
        long estimatedInputTokens = request.Messages.Sum(message =>
            (long)Encoding.UTF8.GetByteCount(message.Role.ToString()) +
            Encoding.UTF8.GetByteCount(message.Content) +
            (message.Image is null ? 0 : message.Image.Base64.Value.Length / 4) +
            (message.ToolCalls?.Sum(call =>
                Encoding.UTF8.GetByteCount(call.Id.Value) +
                Encoding.UTF8.GetByteCount(call.Name.Value) +
                Encoding.UTF8.GetByteCount(call.Arguments.Value)) ?? 0) +
            (message.ToolResult is null
                ? 0
                : Encoding.UTF8.GetByteCount(message.ToolResult.CallId.Value) +
                  Encoding.UTF8.GetByteCount(message.ToolResult.Result.Value)));
        estimatedInputTokens += request.Tools?.Sum(tool =>
            (long)Encoding.UTF8.GetByteCount(tool.Name.Value) +
            Encoding.UTF8.GetByteCount(tool.Description.Value) +
            Encoding.UTF8.GetByteCount(tool.JsonSchema.Value)) ?? 0;
        decimal baseUsd = pricing.UsdPerRequest +
            (estimatedInputTokens * pricing.InputUsdPerToken);
        RemoteCostLedger? ledger = await remoteCostStore.GetLedgerAsync(
            request.RemoteScope!.GoalId,
            cancellationToken);
        if (ledger is null || ledger.CostCap.Value == long.MaxValue ||
            pricing.OutputUsdPerToken == 0)
        {
            return new(
                ToMicroUsdCeiling(baseUsd),
                ProviderMaximumOutputTokens: null,
                IsCostConstrained: false);
        }

        decimal remainingUsd = ledger.RemainingCost.Value / 1_000_000m;
        decimal affordable = decimal.Floor(
            (remainingUsd - baseUsd) / pricing.OutputUsdPerToken);
        if (affordable < 1)
        {
            return new(
                new(checked(ledger.RemainingCost.Value + 1)),
                ProviderMaximumOutputTokens: 1,
                IsCostConstrained: true);
        }

        int modelBoundary;
        lock (pricingLock)
        {
            modelBoundary = contextLengthByModel.GetValueOrDefault(
                request.Model,
                int.MaxValue);
        }
        int providerMaximum = (int)Math.Min(affordable, modelBoundary);
        return new(
            ToMicroUsdCeiling(baseUsd + (providerMaximum * pricing.OutputUsdPerToken)),
            providerMaximum,
            IsCostConstrained: affordable <= modelBoundary);
    }

    private static MicroUsd EstimateEmbeddingCost(EmbeddingRequest request, ModelPricing pricing)
    {
        long estimatedInputTokens = request.Inputs.Sum(input =>
            (long)Encoding.UTF8.GetByteCount(input));
        decimal usd = pricing.UsdPerRequest + (estimatedInputTokens * pricing.InputUsdPerToken);
        return ToMicroUsdCeiling(usd);
    }

    private static MicroUsd ToMicroUsdCeiling(decimal usd) =>
        new(checked((long)decimal.Ceiling(usd * 1_000_000m)));

    private static MicroUsd? ToMicroUsd(decimal? usd) =>
        usd is null ? null : ToMicroUsdCeiling(usd.Value);

    private static OpenRouterProviderPreferences? CreateRouting(ProviderPrivacyPolicy policy) =>
        policy is ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention
            ? new() { Zdr = true }
            : null;

    private static ProviderError? ValidateRemoteRequest(RemoteModelScope? scope)
    {
        if (scope is null || string.IsNullOrWhiteSpace(scope.GoalId))
        {
            return new(
                "remote_scope_required",
                "OpenRouter requests require an approved goal scope.",
                IsTransient: false);
        }

        return null;
    }

    private sealed record ChatCostBoundary(
        MicroUsd EstimatedCost,
        int? ProviderMaximumOutputTokens,
        bool IsCostConstrained);

}
