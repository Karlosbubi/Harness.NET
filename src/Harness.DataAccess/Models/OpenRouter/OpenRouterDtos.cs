using System.Text.Json.Serialization;

namespace Harness.DataAccess.Models.OpenRouter;

internal sealed class OpenRouterChatRequestPayload
{
    public string Model { get; init; } = string.Empty;

    public OpenRouterRequestMessage[] Messages { get; init; } = [];

    public bool Stream { get; init; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterProviderPreferences? Provider { get; init; }
}

internal sealed class OpenRouterEmbeddingRequestPayload
{
    public string Model { get; init; } = string.Empty;

    public IReadOnlyList<string> Input { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Dimensions { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterProviderPreferences? Provider { get; init; }
}

internal sealed class OpenRouterRequestMessage
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;
}

internal sealed class OpenRouterProviderPreferences
{
    [JsonPropertyName("data_collection")]
    public string DataCollection { get; init; } = "deny";

    public bool Zdr { get; init; }
}

internal sealed class OpenRouterModelsResponse
{
    public OpenRouterModel[] Data { get; init; } = [];
}

internal sealed class OpenRouterModel
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    [JsonPropertyName("context_length")]
    public int? ContextLength { get; init; }

    public OpenRouterArchitecture? Architecture { get; init; }

    public OpenRouterPricing? Pricing { get; init; }

    [JsonPropertyName("supported_parameters")]
    public string[] SupportedParameters { get; init; } = [];
}

internal sealed class OpenRouterArchitecture
{
    public string? Tokenizer { get; init; }

    [JsonPropertyName("output_modalities")]
    public string[] OutputModalities { get; init; } = [];
}

internal sealed class OpenRouterPricing
{
    public string? Prompt { get; init; }

    public string? Completion { get; init; }

    public string? Request { get; init; }
}

internal sealed class OpenRouterChatChunk
{
    public OpenRouterChoice[] Choices { get; init; } = [];

    public OpenRouterUsage? Usage { get; init; }

    public OpenRouterError? Error { get; init; }
}

internal sealed class OpenRouterChoice
{
    public OpenRouterDelta? Delta { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

internal sealed class OpenRouterDelta
{
    public string? Content { get; init; }

    public string? Reasoning { get; init; }
}

internal sealed class OpenRouterEmbeddingResponse
{
    public OpenRouterEmbedding[] Data { get; init; } = [];

    public OpenRouterUsage? Usage { get; init; }

    public OpenRouterError? Error { get; init; }
}

internal sealed class OpenRouterEmbedding
{
    public int Index { get; init; }

    public float[] Embedding { get; init; } = [];
}

internal sealed class OpenRouterUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    public decimal? Cost { get; init; }
}

internal sealed class OpenRouterErrorResponse
{
    public OpenRouterError? Error { get; init; }
}

internal sealed class OpenRouterError
{
    public string? Message { get; init; }

    public int? Code { get; init; }
}
