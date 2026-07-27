using System.Text.Json.Serialization;

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
