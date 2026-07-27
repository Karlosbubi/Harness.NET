namespace Harness.DataAccess.Models;

public sealed record ProviderError(
    string Code,
    string Message,
    bool IsTransient);
