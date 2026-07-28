using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Secrets;

namespace Harness.DataAccess.Models.OpenRouter;

internal sealed class OpenRouterModelProvider(
    HttpClient httpClient,
    string providerName,
    ISecretStore secretStore,
    SecretReference apiKeyReference,
    IRemoteCostStore remoteCostStore) : IModelProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, ModelPricing> pricingByModel =
        new(StringComparer.Ordinal);
    private readonly Lock pricingLock = new();

    public async ValueTask<ModelCatalog> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        string? apiKey = await secretStore.GetAsync(apiKeyReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new([], MissingCredential());
        }

        (IReadOnlyList<ModelDescriptor> chatModels, ProviderError? chatError) =
            await GetCatalogAsync(
                "api/v1/models?output_modalities=text",
                ModelPurpose.Chat,
                apiKey,
                cancellationToken);
        if (chatError is not null)
        {
            return new([], chatError);
        }

        (IReadOnlyList<ModelDescriptor> embeddingModels, ProviderError? embeddingError) =
            await GetCatalogAsync(
                "api/v1/embeddings/models",
                ModelPurpose.Embedding,
                apiKey,
                cancellationToken);
        if (embeddingError is not null)
        {
            return new([], embeddingError);
        }

        ModelDescriptor[] models = chatModels
            .Concat(embeddingModels)
            .GroupBy(model => model.Id, StringComparer.Ordinal)
            .Select(group => group.Aggregate(Merge))
            .ToArray();
        lock (pricingLock)
        {
            foreach (ModelDescriptor model in models)
            {
                if (model.Pricing is not null)
                {
                    pricingByModel[model.Id] = model.Pricing;
                }
            }
        }

        return new(models, Error: null);
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ProviderError? validationError = ValidateRemoteRequest(
            request.RemoteScope,
            request.MaximumOutputTokens,
            requireMaximumOutputTokens: true);
        if (validationError is not null)
        {
            yield return ErrorEvent(validationError);
            yield break;
        }

        string? apiKey = await secretStore.GetAsync(apiKeyReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return ErrorEvent(MissingCredential());
            yield break;
        }

        ModelPricingResult pricing = await GetPricingAsync(request.Model, apiKey, cancellationToken);
        if (pricing.Error is not null)
        {
            yield return ErrorEvent(pricing.Error);
            yield break;
        }

        MicroUsd estimate = EstimateChatCost(request, pricing.Pricing!);
        RemoteCostReservationResult reservationResult = await remoteCostStore.ReserveAsync(new(
            request.RemoteScope!.GoalId,
            providerName,
            request.Model,
            RemoteCostOperation.Chat,
            estimate,
            request.RemoteScope.Role), cancellationToken);
        if (reservationResult.Reservation is null)
        {
            yield return ErrorEvent(ReservationError(reservationResult.Failure));
            yield break;
        }

        RemoteCostReservation reservation = reservationResult.Reservation;
        bool requestAccepted = false;
        bool completed = false;
        MicroUsd? actualCost = null;
        try
        {
            using HttpRequestMessage message = CreateAuthorizedRequest(
                HttpMethod.Post,
                "api/v1/chat/completions",
                apiKey);
            message.Content = JsonContent.Create(new OpenRouterChatRequestPayload
            {
                Model = request.Model,
                Messages = request.Messages.Select(MapMessage).ToArray(),
                Tools = MapTools(request.Tools),
                Stream = true,
                MaxTokens = request.MaximumOutputTokens!.Value,
                Provider = CreateRouting(request.RemoteScope.PrivacyPolicy),
            });

            HttpResponseMessage? response = null;
            ProviderError? transportError = null;
            try
            {
                response = await httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                transportError = TransportError(exception);
            }

            if (transportError is not null)
            {
                yield return ErrorEvent(transportError);
                yield break;
            }

            if (response is null)
            {
                yield return ErrorEvent(MissingResponse());
                yield break;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    yield return ErrorEvent(await ReadErrorAsync(response, cancellationToken));
                    yield break;
                }

                requestAccepted = true;
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using StreamReader reader = new(stream);
                Dictionary<int, OpenRouterToolCallBuilder> toolCalls = [];
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
                    {
                        continue;
                    }

                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string data = line[5..].TrimStart();
                    if (data == "[DONE]")
                    {
                        if (!completed)
                        {
                            completed = true;
                            yield return new(
                                string.Empty,
                                string.Empty,
                                Done: true,
                                DoneReason: null,
                                new(0, 0, actualCost),
                                Error: null,
                                CompleteToolCalls(toolCalls));
                        }

                        break;
                    }

                    OpenRouterChatChunk? chunk = null;
                    ProviderError? parseError = null;
                    try
                    {
                        chunk = JsonSerializer.Deserialize<OpenRouterChatChunk>(data, JsonOptions);
                    }
                    catch (JsonException exception)
                    {
                        parseError = InvalidResponse(exception);
                    }

                    if (parseError is not null)
                    {
                        yield return ErrorEvent(parseError);
                        yield break;
                    }

                    if (chunk?.Error is not null)
                    {
                        yield return ErrorEvent(MapStreamError(chunk.Error));
                        yield break;
                    }

                    if (chunk is null)
                    {
                        continue;
                    }

                    OpenRouterChoice? choice = chunk.Choices.FirstOrDefault();
                    MicroUsd? chunkCost = ToMicroUsd(chunk.Usage?.Cost);
                    actualCost = chunkCost ?? actualCost;
                    bool done = choice?.FinishReason is not null || chunk.Usage is not null;
                    completed |= done;
                    if (choice?.Delta is not null)
                    {
                        AccumulateToolCalls(choice.Delta.ToolCalls, toolCalls);
                    }

                    IReadOnlyList<ChatToolCall>? completedCalls = done
                        ? CompleteToolCalls(toolCalls)
                        : null;
                    if (choice is not null || chunk.Usage is not null)
                    {
                        yield return new(
                            choice?.Delta?.Content ?? string.Empty,
                            choice?.Delta?.Reasoning ?? string.Empty,
                            done,
                            choice?.FinishReason,
                            new(
                                chunk.Usage?.PromptTokens ?? 0,
                                chunk.Usage?.CompletionTokens ?? 0,
                                chunkCost),
                            Error: null,
                            completedCalls);
                    }
                }
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

    private static OpenRouterRequestMessage MapMessage(ChatMessage message) => new()
    {
        Role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        },
        Content = message.ToolResult?.Result.Value ??
            (string.IsNullOrEmpty(message.Content) ? null : message.Content),
        ToolCalls = message.ToolCalls?.Select(call => new OpenRouterToolCall
        {
            Id = call.Id.Value,
            Function = new()
            {
                Name = call.Name.Value,
                Arguments = call.Arguments.Value,
            },
        }).ToArray(),
        ToolCallId = message.ToolResult?.CallId.Value,
    };

    private static OpenRouterToolDefinition[]? MapTools(
        IReadOnlyList<ChatToolDefinition>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return null;
        }

        return tools.Select(tool => new OpenRouterToolDefinition
        {
            Function = new()
            {
                Name = tool.Name.Value,
                Description = tool.Description.Value,
                Parameters = JsonDocument.Parse(tool.JsonSchema.Value).RootElement.Clone(),
            },
        }).ToArray();
    }

    private static void AccumulateToolCalls(
        IReadOnlyList<OpenRouterToolCall> deltas,
        Dictionary<int, OpenRouterToolCallBuilder> calls)
    {
        foreach (OpenRouterToolCall delta in deltas)
        {
            int index = delta.Index ?? calls.Count;
            if (!calls.TryGetValue(index, out OpenRouterToolCallBuilder? builder))
            {
                builder = new();
                calls[index] = builder;
            }

            builder.Id ??= delta.Id;
            builder.Name ??= delta.Function.Name;
            builder.Arguments.Append(delta.Function.Arguments);
        }
    }

    private static IReadOnlyList<ChatToolCall>? CompleteToolCalls(
        IReadOnlyDictionary<int, OpenRouterToolCallBuilder> calls) =>
        calls.Count == 0
            ? null
            : calls.OrderBy(item => item.Key)
                .Select(item => new ChatToolCall(
                    new(item.Value.Id ?? $"openrouter-{Guid.NewGuid():N}-{item.Key}"),
                    new(item.Value.Name ?? string.Empty),
                    new(item.Value.Arguments.Length == 0
                        ? "{}"
                        : item.Value.Arguments.ToString())))
                .ToArray();

    private sealed class OpenRouterToolCallBuilder
    {
        internal string? Id { get; set; }

        internal string? Name { get; set; }

        internal StringBuilder Arguments { get; } = new();
    }

    public async ValueTask<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        ProviderError? validationError = ValidateRemoteRequest(
            request.RemoteScope,
            maximumOutputTokens: null,
            requireMaximumOutputTokens: false);
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

    private static MicroUsd EstimateChatCost(ChatRequest request, ModelPricing pricing)
    {
        long estimatedInputTokens = request.Messages.Sum(message =>
            (long)Encoding.UTF8.GetByteCount(message.Role.ToString()) +
            Encoding.UTF8.GetByteCount(message.Content) +
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
        decimal usd = pricing.UsdPerRequest +
            (estimatedInputTokens * pricing.InputUsdPerToken) +
            (request.MaximumOutputTokens!.Value * pricing.OutputUsdPerToken);
        return ToMicroUsdCeiling(usd);
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

    private static ProviderError? ValidateRemoteRequest(
        RemoteModelScope? scope,
        MaximumOutputTokens? maximumOutputTokens,
        bool requireMaximumOutputTokens)
    {
        if (scope is null || string.IsNullOrWhiteSpace(scope.GoalId))
        {
            return new(
                "remote_scope_required",
                "OpenRouter requests require an approved goal scope.",
                IsTransient: false);
        }

        return (requireMaximumOutputTokens && maximumOutputTokens is null) ||
            maximumOutputTokens is not null && maximumOutputTokens.Value <= 0
            ? new(
                "maximum_output_tokens_required",
                "OpenRouter chat requests require a positive output-token maximum.",
                IsTransient: false)
            : null;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string apiKey)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static ChatStreamEvent ErrorEvent(ProviderError error) =>
        new(string.Empty, string.Empty, Done: true, "error", new(0, 0), error);

    private static EmbeddingResult EmbeddingFailure(ProviderError error) =>
        new([], new(0, 0), error);

    private static ProviderError ReservationError(RemoteCostReservationFailure? failure) =>
        failure is RemoteCostReservationFailure.CostCapExceeded
            ? new("remote_cost_cap_exceeded", "The goal's remote-model cost cap is exhausted.", false)
            : new("remote_model_not_authorized", "The goal is not approved for remote-model use.", false);

    private static ProviderError MissingCredential() =>
        new("credential_missing", "The OpenRouter API key is unavailable.", IsTransient: false);

    private static ProviderError TransportError(HttpRequestException exception) =>
        new("transport_error", exception.Message, IsTransient: true);

    private static ProviderError InvalidResponse(JsonException exception) =>
        new("invalid_response", exception.Message, IsTransient: false);

    private static ProviderError MissingResponse() =>
        new("missing_response", "The provider returned no response.", IsTransient: true);

    private static ProviderError MapStreamError(OpenRouterError error) =>
        new(
            error.Code is null ? "stream_error" : $"provider_{error.Code.Value}",
            error.Message ?? "OpenRouter reported a streaming error.",
            error.Code is 408 or 429 or >= 500);

    private static async ValueTask<ProviderError> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string message = response.ReasonPhrase ?? "Provider request failed.";
        try
        {
            OpenRouterErrorResponse? error = await response.Content
                .ReadFromJsonAsync<OpenRouterErrorResponse>(JsonOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(error?.Error?.Message))
            {
                message = error.Error.Message;
            }
        }
        catch (JsonException)
        {
        }

        int status = (int)response.StatusCode;
        bool transient = response.StatusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests || status >= 500;
        return new($"http_{status}", message, transient);
    }

    private sealed record ModelPricingResult(ModelPricing? Pricing, ProviderError? Error);
}
