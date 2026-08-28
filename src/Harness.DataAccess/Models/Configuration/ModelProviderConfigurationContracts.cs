using Harness.DataAccess.Secrets;

namespace Harness.DataAccess.Models.Configuration;

public enum StoredModelProviderKind
{
    Ollama,
    OpenRouter,
}

public sealed record StoredModelProviderName(string Value);

public sealed record StoredModelProviderEndpoint(Uri Value);

public sealed record StoredModelProviderModel(string Value);

public sealed record StoredEmbeddingDimensions(int Value);

public sealed record StoredAgentContextTokenLimit(int Value);

public sealed record StoredProviderTimeout(TimeSpan Value);

public sealed record StoredModelProviderConfiguration(
    StoredModelProviderName Name,
    StoredModelProviderKind Kind,
    StoredModelProviderEndpoint Endpoint,
    StoredModelProviderModel ChatModel,
    StoredModelProviderModel EmbeddingModel,
    StoredEmbeddingDimensions EmbeddingDimensions,
    StoredAgentContextTokenLimit? MaximumAgentContextTokens,
    StoredProviderTimeout ConnectTimeout,
    StoredProviderTimeout RequestTimeout,
    SecretReference? ApiKeyReference,
    bool RequiresRestart);

public sealed record ModelProviderConfigurationOptions(
    IReadOnlyList<StoredModelProviderConfiguration> Providers);

public interface IModelProviderConfigurationStore
{
    ValueTask<IReadOnlyList<StoredModelProviderConfiguration>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredModelProviderConfiguration> SaveAsync(
        StoredModelProviderConfiguration configuration,
        CancellationToken cancellationToken = default);
}
