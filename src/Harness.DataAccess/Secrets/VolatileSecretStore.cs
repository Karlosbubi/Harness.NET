using System.Collections.Concurrent;

namespace Harness.DataAccess.Secrets;

internal sealed class VolatileSecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> values = new(StringComparer.Ordinal);

    public ValueTask<string?> GetAsync(
        SecretReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(values.GetValueOrDefault(reference.Name));
    }

    public ValueTask SetAsync(
        SecretReference reference, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        values[reference.Name] = value;
        return ValueTask.CompletedTask;
    }
}
