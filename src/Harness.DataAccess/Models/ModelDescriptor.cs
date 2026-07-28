namespace Harness.DataAccess.Models;

public sealed record ModelDescriptor(
    string Id,
    string Provider,
    string? Family,
    string? ParameterSize,
    string? Quantization,
    IReadOnlyList<string> Capabilities,
    int? ContextLength = null,
    ModelPricing? Pricing = null,
    IReadOnlyList<ModelPurpose>? Purposes = null);
