using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Secrets;

namespace Harness.DataAccess.Models.OpenRouter;

internal sealed partial class OpenRouterModelProvider(
    HttpClient httpClient,
    string providerName,
    ISecretStore secretStore,
    SecretReference apiKeyReference,
    IRemoteCostStore remoteCostStore) : IModelProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, ModelPricing> pricingByModel =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> contextLengthByModel =
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
                if (model.ContextLength is > 0)
                {
                    contextLengthByModel[model.Id] = model.ContextLength.Value;
                }
            }
        }

        return new(models, Error: null);
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ProviderError? validationError = ValidateRemoteRequest(request.RemoteScope);
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

        ChatCostBoundary boundary = await ResolveChatCostBoundaryAsync(
            request,
            pricing.Pricing!,
            cancellationToken);
        RemoteCostReservationResult reservationResult = await remoteCostStore.ReserveAsync(new(
            request.RemoteScope!.GoalId,
            providerName,
            request.Model,
            RemoteCostOperation.Chat,
            boundary.EstimatedCost,
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
                MaxTokens = boundary.ProviderMaximumOutputTokens,
                Provider = CreateRouting(request.RemoteScope.PrivacyPolicy),
                Reasoning = MapReasoning(request.ReasoningEffort),
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
                List<JsonElement> reasoningDetails = [];
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
                                CompleteToolCalls(toolCalls),
                                SerializeReasoningDetails(reasoningDetails));
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
                    if (boundary.IsCostConstrained &&
                        choice?.FinishReason is "length")
                    {
                        yield return ErrorEvent(new(
                            "remote_cost_cap_exceeded",
                            "The model reached the output boundary derived from the goal's remaining monetary cost cap.",
                            IsTransient: false));
                        yield break;
                    }
                    bool done = choice?.FinishReason is not null || chunk.Usage is not null;
                    bool firstCompletion = done && !completed;
                    completed |= done;
                    if (choice?.Delta is not null)
                    {
                        AccumulateToolCalls(choice.Delta.ToolCalls, toolCalls);
                        reasoningDetails.AddRange(choice.Delta.ReasoningDetails.Select(
                            detail => detail.Clone()));
                    }

                    IReadOnlyList<ChatToolCall>? completedCalls = firstCompletion
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
                            completedCalls,
                            firstCompletion ? SerializeReasoningDetails(reasoningDetails) : null);
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
        Content = message.Image is null
            ? message.ToolResult?.Result.Value ??
              (string.IsNullOrEmpty(message.Content) ? null : message.Content)
            : new object[]
            {
                new OpenRouterTextContent { Text = message.Content },
                new OpenRouterImageContent
                {
                    ImageUrl = new()
                    {
                        Url = $"data:{message.Image.MediaType.Value};base64,{message.Image.Base64.Value}",
                    },
                },
            },
        Reasoning = string.IsNullOrEmpty(message.Reasoning?.Text.Value)
            ? null
            : message.Reasoning.Text.Value,
        ReasoningDetails = ParseReasoningDetails(message.Reasoning?.Details),
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

    private static OpenRouterReasoningOptions? MapReasoning(ModelReasoningEffort effort) =>
        effort switch
        {
            ModelReasoningEffort.ProviderDefault => null,
            ModelReasoningEffort.None => new() { Enabled = false },
            ModelReasoningEffort.Low => new() { Effort = "low" },
            ModelReasoningEffort.Medium => new() { Effort = "medium" },
            ModelReasoningEffort.High => new() { Effort = "high" },
            _ => throw new ArgumentOutOfRangeException(nameof(effort)),
        };

    private static JsonElement? ParseReasoningDetails(ChatReasoningDetailsJson? details)
    {
        if (details is null)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(details.Value);
        return document.RootElement.Clone();
    }

    private static ChatReasoningDetailsJson? SerializeReasoningDetails(
        IReadOnlyList<JsonElement> details) => details.Count == 0
            ? null
            : new(JsonSerializer.Serialize(details, JsonOptions));

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
