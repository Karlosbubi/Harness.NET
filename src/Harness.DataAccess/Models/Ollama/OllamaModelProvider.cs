using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Harness.DataAccess.Models.Ollama;

internal sealed record OllamaContextTokenLimit(int Value);

internal sealed class OllamaModelProvider(
    HttpClient httpClient,
    OllamaContextTokenLimit? maximumAgentContextTokens = null) : IModelProvider
{
    private const string ProviderName = "Ollama";
    private const int MinimumAgentContextLength = 4_096;
    private const int DefaultMaximumAgentContextLength = 8_192;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, int> contextLengthByModel =
        new(StringComparer.Ordinal);
    private readonly Lock contextLengthLock = new();

    public async ValueTask<ModelCatalog> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage? response = null;
        ProviderError? transportError = null;
        try
        {
            response = await httpClient.GetAsync("api/tags", cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            transportError = TransportError(exception);
        }

        if (transportError is not null)
        {
            return new([], transportError);
        }

        if (response is null)
        {
            return new([], MissingResponse());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new([], await ReadErrorAsync(response, cancellationToken));
            }

            try
            {
                OllamaTagsResponse? payload = await response.Content
                    .ReadFromJsonAsync<OllamaTagsResponse>(JsonOptions, cancellationToken);
                ModelDescriptor[] models = payload?.Models
                    .Select(model => new ModelDescriptor(
                        model.Model ?? model.Name ?? string.Empty,
                        ProviderName,
                        model.Details?.Family,
                        model.Details?.ParameterSize,
                        model.Details?.QuantizationLevel,
                        model.Capabilities,
                        ContextLength: model.Details?.ContextLength,
                        Purposes: Purposes(model.Capabilities)))
                    .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                    .ToArray() ?? [];
                lock (contextLengthLock)
                {
                    foreach (ModelDescriptor model in models)
                    {
                        if (model.ContextLength is > 0)
                        {
                            contextLengthByModel[model.Id] = model.ContextLength.Value;
                        }
                    }
                }
                return new(models, Error: null);
            }
            catch (JsonException exception)
            {
                return new([], InvalidResponse(exception));
            }
        }
    }

    private static IReadOnlyList<ModelPurpose> Purposes(IReadOnlyList<string> capabilities) =>
        capabilities.SelectMany(capability => capability switch
            {
                "completion" => [ModelPurpose.Chat],
                "embedding" => [ModelPurpose.Embedding],
                _ => Array.Empty<ModelPurpose>(),
            })
            .Distinct()
            .ToArray();

    public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(new OllamaChatRequestPayload
            {
                Model = request.Model,
                Messages = request.Messages.Select(MapMessage).ToArray(),
                Stream = true,
                Format = ResponseFormat(request),
                Tools = MapTools(request.Tools),
                Think = ReasoningValue(request.ReasoningEffort),
                Options = new()
                {
                    ContextLength = ResolveContextLength(request),
                    Temperature = request.Temperature ??
                        (request.Tools is { Count: > 0 } ? 0 : null),
                },
            }),
        };

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

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using StreamReader reader = new(stream);
            List<ChatToolCall> accumulatedToolCalls = [];
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                OllamaChatResponse? chunk = null;
                ProviderError? parseError = null;
                try
                {
                    chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
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

                if (!string.IsNullOrWhiteSpace(chunk?.Error))
                {
                    yield return ErrorEvent(new("stream_error", chunk.Error, IsTransient: false));
                    yield break;
                }

                if (chunk is not null)
                {
                    ChatToolCall[] toolCalls = chunk.Message?.ToolCalls
                        .Select(call => new ChatToolCall(
                            new($"ollama-{Guid.NewGuid():N}"),
                            new(call.Function.Name),
                            new(call.Function.Arguments.ValueKind is JsonValueKind.Undefined
                                ? "{}"
                                : call.Function.Arguments.GetRawText())))
                        .ToArray() ?? [];
                    accumulatedToolCalls.AddRange(toolCalls);
                    yield return new(
                        chunk.Message?.Content ?? string.Empty,
                        chunk.Message?.Thinking ?? string.Empty,
                        chunk.Done,
                        chunk.DoneReason,
                        new(chunk.PromptEvalCount, chunk.EvalCount),
                        Error: null,
                        chunk.Done && accumulatedToolCalls.Count > 0
                            ? accumulatedToolCalls.ToArray()
                            : null);
                }
            }
        }
    }

    private static OllamaRequestMessage MapMessage(ChatMessage message) => new()
    {
        Role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        },
        Content = message.ToolResult?.Result.Value ?? message.Content,
        Images = message.Image is null ? null : [message.Image.Base64.Value],
        Thinking = message.Reasoning?.Text.Value,
        ToolName = message.ToolResult?.ToolName?.Value,
        ToolCalls = message.ToolCalls?.Select(call => new OllamaToolCall
        {
            Function = new()
            {
                Name = call.Name.Value,
                Arguments = JsonDocument.Parse(call.Arguments.Value).RootElement.Clone(),
            },
        }).ToArray(),
    };

    private static JsonElement? ReasoningValue(ModelReasoningEffort effort) => effort switch
    {
        ModelReasoningEffort.ProviderDefault => null,
        ModelReasoningEffort.None => JsonSerializer.SerializeToElement(false),
        ModelReasoningEffort.Low => JsonSerializer.SerializeToElement("low"),
        ModelReasoningEffort.Medium => JsonSerializer.SerializeToElement("medium"),
        ModelReasoningEffort.High => JsonSerializer.SerializeToElement("high"),
        _ => throw new ArgumentOutOfRangeException(nameof(effort)),
    };

    private static JsonElement? ResponseFormat(ChatRequest request)
    {
        if (request.ResponseSchema is not null)
        {
            using JsonDocument schema = JsonDocument.Parse(request.ResponseSchema.Value);
            return schema.RootElement.Clone();
        }

        if (request.ResponseFormat is not ChatResponseFormat.Json)
        {
            return null;
        }

        using JsonDocument json = JsonDocument.Parse("\"json\"");
        return json.RootElement.Clone();
    }

    private static OllamaToolDefinition[]? MapTools(
        IReadOnlyList<ChatToolDefinition>? tools) =>
        tools is null || tools.Count == 0
            ? null
            : tools.Select(tool => new OllamaToolDefinition
            {
                Function = new()
                {
                    Name = tool.Name.Value,
                    Description = tool.Description.Value,
                    Parameters = JsonDocument.Parse(tool.JsonSchema.Value).RootElement.Clone(),
                },
            }).ToArray();

    private int ResolveContextLength(ChatRequest request)
    {
        long characterCount = request.Messages.Sum(message => (long)message.Content.Length) +
                              (request.ResponseSchema?.Value.Length ?? 0) +
                              (request.Tools?.Sum(tool =>
                                  (long)tool.Description.Value.Length + tool.JsonSchema.Value.Length) ?? 0);
        long estimatedInputTokens = (characterCount + 2) / 3;
        int desired = (int)Math.Min(
            int.MaxValue,
            Math.Max(MinimumAgentContextLength, (estimatedInputTokens * 2) + 4_096));
        int configured = Math.Min(
            desired,
            maximumAgentContextTokens?.Value ?? DefaultMaximumAgentContextLength);
        lock (contextLengthLock)
        {
            return contextLengthByModel.TryGetValue(request.Model, out int advertisedMaximum)
                ? Math.Min(configured, advertisedMaximum)
                : configured;
        }
    }

    public async ValueTask<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage? response = null;
        ProviderError? transportError = null;
        try
        {
            response = await httpClient.PostAsJsonAsync("api/embed", new
            {
                model = request.Model,
                input = request.Inputs,
                dimensions = request.Dimensions,
            }, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            transportError = TransportError(exception);
        }

        if (transportError is not null)
        {
            return new([], new(0, 0), transportError);
        }

        if (response is null)
        {
            return new([], new(0, 0), MissingResponse());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new([], new(0, 0), await ReadErrorAsync(response, cancellationToken));
            }

            try
            {
                OllamaEmbeddingResponse? payload = await response.Content
                    .ReadFromJsonAsync<OllamaEmbeddingResponse>(JsonOptions, cancellationToken);
                IReadOnlyList<IReadOnlyList<float>> embeddings = payload?.Embeddings
                    .Select(vector => (IReadOnlyList<float>)vector)
                    .ToArray() ?? [];
                return new(embeddings, new(payload?.PromptEvalCount ?? 0, 0), Error: null);
            }
            catch (JsonException exception)
            {
                return new([], new(0, 0), InvalidResponse(exception));
            }
        }
    }

    private static ChatStreamEvent ErrorEvent(ProviderError error) =>
        new(string.Empty, string.Empty, Done: true, "error", new(0, 0), error);

    private static ProviderError TransportError(HttpRequestException exception) =>
        new("transport_error", exception.Message, IsTransient: true);

    private static ProviderError InvalidResponse(JsonException exception) =>
        new("invalid_response", exception.Message, IsTransient: false);

    private static ProviderError MissingResponse() =>
        new("missing_response", "The provider returned no response.", IsTransient: true);

    private static async ValueTask<ProviderError> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string message = response.ReasonPhrase ?? "Provider request failed.";
        try
        {
            OllamaErrorResponse? error = await response.Content
                .ReadFromJsonAsync<OllamaErrorResponse>(JsonOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                message = error.Error;
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
}
