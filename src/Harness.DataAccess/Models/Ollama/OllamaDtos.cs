using System.Text.Json.Serialization;
using System.Text.Json;

namespace Harness.DataAccess.Models.Ollama;

internal sealed class OllamaTagsResponse
{
    public OllamaModel[] Models { get; init; } = [];
}

internal sealed class OllamaModel
{
    public string? Name { get; init; }

    public string? Model { get; init; }

    public OllamaModelDetails? Details { get; init; }

    public string[] Capabilities { get; init; } = [];
}

internal sealed class OllamaModelDetails
{
    public string? Family { get; init; }

    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; init; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; init; }

    [JsonPropertyName("context_length")]
    public int? ContextLength { get; init; }
}

internal sealed class OllamaChatResponse
{
    public OllamaResponseMessage? Message { get; init; }

    public bool Done { get; init; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; init; }

    [JsonPropertyName("eval_count")]
    public int EvalCount { get; init; }

    public string? Error { get; init; }
}

internal sealed class OllamaResponseMessage
{
    public string? Content { get; init; }

    public string? Thinking { get; init; }

    [JsonPropertyName("tool_calls")]
    public OllamaToolCall[] ToolCalls { get; init; } = [];
}

internal sealed class OllamaChatRequestPayload
{
    public string Model { get; init; } = string.Empty;

    public OllamaRequestMessage[] Messages { get; init; } = [];

    public bool Stream { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Format { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OllamaToolDefinition[]? Tools { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Think { get; init; }

    public OllamaChatOptions Options { get; init; } = new();
}

internal sealed class OllamaChatOptions
{
    [JsonPropertyName("num_ctx")]
    public int ContextLength { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; init; }
}

internal sealed class OllamaRequestMessage
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OllamaToolCall[]? ToolCalls { get; init; }
}

internal sealed class OllamaToolDefinition
{
    public string Type { get; init; } = "function";

    public OllamaFunctionDefinition Function { get; init; } = new();
}

internal sealed class OllamaFunctionDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public JsonElement Parameters { get; init; }
}

internal sealed class OllamaToolCall
{
    public OllamaFunctionCall Function { get; init; } = new();
}

internal sealed class OllamaFunctionCall
{
    public string Name { get; init; } = string.Empty;

    public JsonElement Arguments { get; init; }
}

internal sealed class OllamaEmbeddingResponse
{
    public float[][] Embeddings { get; init; } = [];

    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; init; }
}

internal sealed class OllamaErrorResponse
{
    public string? Error { get; init; }
}
