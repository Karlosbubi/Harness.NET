namespace Harness.DataAccess.Secrets;

internal sealed class SecretServiceSecretStore : ISecretStore
{
    private readonly ISecretServiceClient secretServiceClient;
    private readonly Func<string, string?> getEnvironmentVariable;

    public SecretServiceSecretStore()
        : this(new SecretServiceClient(), Environment.GetEnvironmentVariable)
    {
    }

    internal SecretServiceSecretStore(
        ISecretServiceClient secretServiceClient,
        Func<string, string?> getEnvironmentVariable)
    {
        this.secretServiceClient = secretServiceClient;
        this.getEnvironmentVariable = getEnvironmentVariable;
    }

    public ValueTask<string?> GetAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default)
    {
        if (reference.EnvironmentVariable is not null)
        {
            string? environmentValue = getEnvironmentVariable(reference.EnvironmentVariable);
            if (!string.IsNullOrEmpty(environmentValue))
            {
                return ValueTask.FromResult<string?>(environmentValue);
            }
        }

        return secretServiceClient.GetAsync(reference.Name, cancellationToken);
    }

    public ValueTask SetAsync(
        SecretReference reference,
        string value,
        CancellationToken cancellationToken = default) =>
        secretServiceClient.SetAsync(reference.Name, value, cancellationToken);
}
