using System.Text.Json.Serialization;
using System.Text.Json;

namespace Harness.DataAccess.Models.OpenRouter;

internal sealed class OpenRouterChatRequestPayload
{
    public string Model { get; init; } = string.Empty;

    public OpenRouterRequestMessage[] Messages { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterToolDefinition[]? Tools { get; init; }

    public bool Stream { get; init; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterProviderPreferences? Provider { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterReasoningOptions? Reasoning { get; init; }
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reasoning { get; init; }

    [JsonPropertyName("reasoning_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ReasoningDetails { get; init; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterToolCall[]? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }
}

internal sealed class OpenRouterTextContent
{
    public string Type { get; init; } = "text";
    public string Text { get; init; } = string.Empty;
}

internal sealed class OpenRouterImageContent
{
    public string Type { get; init; } = "image_url";

    [JsonPropertyName("image_url")]
    public OpenRouterImageUrl ImageUrl { get; init; } = new();
}

internal sealed class OpenRouterImageUrl
{
    public string Url { get; init; } = string.Empty;
}

internal sealed class OpenRouterReasoningOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Effort { get; init; }
}

internal sealed class OpenRouterToolDefinition
{
    public string Type { get; init; } = "function";

    public OpenRouterFunctionDefinition Function { get; init; } = new();
}

internal sealed class OpenRouterFunctionDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public JsonElement Parameters { get; init; }
}

internal sealed class OpenRouterToolCall
{
    public int? Index { get; init; }

    public string? Id { get; init; }

    public string Type { get; init; } = "function";

    public OpenRouterFunctionCall Function { get; init; } = new();
}

internal sealed class OpenRouterFunctionCall
{
    public string? Name { get; init; }

    public string? Arguments { get; init; }
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

    [JsonPropertyName("reasoning_details")]
    public JsonElement[] ReasoningDetails { get; init; } = [];

    [JsonPropertyName("tool_calls")]
    public OpenRouterToolCall[] ToolCalls { get; init; } = [];
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
