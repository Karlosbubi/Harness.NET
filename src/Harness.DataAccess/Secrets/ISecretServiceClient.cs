namespace Harness.DataAccess.Secrets;

internal interface ISecretServiceClient
{
    ValueTask<string?> GetAsync(string name, CancellationToken cancellationToken);

    ValueTask SetAsync(string name, string value, CancellationToken cancellationToken);
}
