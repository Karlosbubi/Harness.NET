using Harness.BusinessLogic.Agents;
using Harness.DataAccess.Models.Configuration;
using Harness.DataAccess.Secrets;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class ModelProviderSettingsServiceTests
{
    [Fact]
    public async Task Validates_and_persists_provider_configuration_as_restart_required()
    {
        MemoryConfigurationStore configurations = new([
            Provider("Ollama", StoredModelProviderKind.Ollama),
        ]);
        ModelProviderSettingsService service = new(configurations, new MemorySecretStore());

        ModelProviderSettingsResult invalid = await service.UpdateAsync(new(
            new("Ollama"), new("not-a-uri"), new("chat"), new("embed"),
            new(768), new(5), new(600), null, null));
        ModelProviderSettingsResult saved = await service.UpdateAsync(new(
            new("Ollama"), new("http://localhost:11434"), new("qwen3:latest"),
            new("nomic-embed-text"), new(768), new(10), new(900), null, null));

        Assert.Equal("invalid_provider_configuration", invalid.ErrorCode);
        ModelProviderSettingsView view = Assert.Single(saved.Snapshot!.Providers);
        Assert.Equal("qwen3:latest", view.ChatModel.Value);
        Assert.True(view.RequiresRestart);
        Assert.Equal(1, configurations.SaveCount);
    }

    [Fact]
    public async Task OpenRouter_credential_is_write_only_and_uses_the_configured_secret_reference()
    {
        MemorySecretStore secrets = new();
        ModelProviderSettingsService service = new(
            new MemoryConfigurationStore([
                Provider("OpenRouter", StoredModelProviderKind.OpenRouter) with
                {
                    ApiKeyReference = new("openrouter-key", "OPENROUTER_API_KEY"),
                },
            ]),
            secrets);

        ModelProviderSettingsResult saved = await service.SetCredentialAsync(new(
            new("OpenRouter"),
            new("sk-private-value")));

        Assert.Equal("sk-private-value", secrets.Values["openrouter-key"]);
        ModelProviderSettingsView view = Assert.Single(saved.Snapshot!.Providers);
        Assert.Equal(ModelProviderCredentialState.Configured, view.CredentialState);
        Assert.DoesNotContain("sk-private-value", view.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credential_status_failure_is_reported_without_breaking_settings_startup()
    {
        ModelProviderSettingsService service = new(
            new MemoryConfigurationStore([
                Provider("OpenRouter", StoredModelProviderKind.OpenRouter) with
                {
                    ApiKeyReference = new("openrouter-key", "OPENROUTER_API_KEY"),
                },
            ]),
            new MemorySecretStore { ReadFailure = new InvalidOperationException("Secret Service unavailable") });

        ModelProviderSettingsView view = Assert.Single((await service.GetAsync()).Providers);

        Assert.Equal(ModelProviderCredentialState.Unavailable, view.CredentialState);
        Assert.Equal("Secret Service unavailable", view.CredentialMessage);
    }

    private static StoredModelProviderConfiguration Provider(
        string name,
        StoredModelProviderKind kind) => new(
        new(name),
        kind,
        new(new Uri(kind is StoredModelProviderKind.Ollama
            ? "http://localhost:11434"
            : "https://openrouter.ai")),
        new("chat"),
        new("embedding"),
        new(768),
        new(TimeSpan.FromSeconds(5)),
        new(TimeSpan.FromSeconds(600)),
        ApiKeyReference: null,
        RequiresRestart: false);

    private sealed class MemoryConfigurationStore(
        IReadOnlyList<StoredModelProviderConfiguration> initial) : IModelProviderConfigurationStore
    {
        private readonly Dictionary<string, StoredModelProviderConfiguration> values = initial
            .ToDictionary(provider => provider.Name.Value, StringComparer.OrdinalIgnoreCase);

        public int SaveCount { get; private set; }

        public ValueTask<IReadOnlyList<StoredModelProviderConfiguration>> ListAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredModelProviderConfiguration>>(
                values.Values.ToArray());

        public ValueTask<StoredModelProviderConfiguration> SaveAsync(
            StoredModelProviderConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            StoredModelProviderConfiguration saved = configuration with { RequiresRestart = true };
            values[configuration.Name.Value] = saved;
            return ValueTask.FromResult(saved);
        }
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public Exception? ReadFailure { get; init; }

        public ValueTask<string?> GetAsync(
            SecretReference reference,
            CancellationToken cancellationToken = default)
        {
            if (ReadFailure is not null)
            {
                return ValueTask.FromException<string?>(ReadFailure);
            }

            return ValueTask.FromResult(Values.GetValueOrDefault(reference.Name));
        }

        public ValueTask SetAsync(
            SecretReference reference,
            string value,
            CancellationToken cancellationToken = default)
        {
            Values[reference.Name] = value;
            return ValueTask.CompletedTask;
        }
    }
}
