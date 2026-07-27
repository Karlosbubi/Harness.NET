namespace Harness.BusinessLogic.Dashboard;

public sealed record ProviderModel(
    string Id,
    string? Family,
    string? ParameterSize,
    string? Quantization,
    IReadOnlyList<string> Capabilities);
