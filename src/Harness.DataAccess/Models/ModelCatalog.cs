namespace Harness.DataAccess.Models;

public sealed record ModelCatalog(
    IReadOnlyList<ModelDescriptor> Models,
    ProviderError? Error);
