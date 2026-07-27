using Harness.DataAccess.Secrets;

namespace Harness.DataAccess.Tests.Secrets;

public sealed class SecretServiceSecretStoreTests
{
    [Fact]
    public async Task Environment_value_takes_precedence_over_keyring()
    {
        RecordingSecretServiceClient client = new("keyring-value");
        SecretServiceSecretStore store = new(
            client,
            name => name == "OPENROUTER_API_KEY" ? "environment-value" : null);

        string? value = await store.GetAsync(
            new SecretReference("openrouter", "OPENROUTER_API_KEY"));

        Assert.Equal("environment-value", value);
        Assert.Equal(0, client.GetCallCount);
    }

    [Fact]
    public async Task Falls_back_to_keyring_when_environment_value_is_missing()
    {
        RecordingSecretServiceClient client = new("keyring-value");
        SecretServiceSecretStore store = new(client, _ => null);

        string? value = await store.GetAsync(
            new SecretReference("openrouter", "OPENROUTER_API_KEY"));

        Assert.Equal("keyring-value", value);
        Assert.Equal(1, client.GetCallCount);
    }

    private sealed class RecordingSecretServiceClient(string value) : ISecretServiceClient
    {
        public int GetCallCount { get; private set; }

        public ValueTask<string?> GetAsync(string name, CancellationToken cancellationToken)
        {
            GetCallCount++;
            return ValueTask.FromResult<string?>(value);
        }

        public ValueTask SetAsync(string name, string secret, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
