namespace Harness.DataAccess.Secrets;

public interface ISecretStore
{
    ValueTask<string?> GetAsync(SecretReference reference, CancellationToken cancellationToken = default);

    ValueTask SetAsync(
        SecretReference reference,
        string value,
        CancellationToken cancellationToken = default);
}
