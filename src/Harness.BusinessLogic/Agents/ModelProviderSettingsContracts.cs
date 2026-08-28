namespace Harness.BusinessLogic.Agents;

public enum AgentModelProviderKind
{
    Ollama,
    OpenRouter,
}

public enum ModelProviderCredentialState
{
    NotApplicable,
    Missing,
    Configured,
    Unavailable,
}

public sealed record ModelProviderEndpoint(string Value);

public sealed record EmbeddingModel(string Value);

public sealed record EmbeddingDimensions(int Value);

public sealed record AgentContextTokenLimit(int Value);

public sealed record ProviderTimeoutSeconds(int Value);

public sealed record ModelProviderSecretName(string Value);

public sealed record ModelProviderEnvironmentVariable(string Value);

public sealed record ModelProviderCredential(string Value);

public sealed record ModelProviderSettingsView(
    ModelProviderName Provider,
    AgentModelProviderKind Kind,
    ModelProviderEndpoint Endpoint,
    AgentModel ChatModel,
    EmbeddingModel EmbeddingModel,
    EmbeddingDimensions EmbeddingDimensions,
    AgentContextTokenLimit? MaximumAgentContextTokens,
    ProviderTimeoutSeconds ConnectTimeout,
    ProviderTimeoutSeconds RequestTimeout,
    ModelProviderSecretName? SecretName,
    ModelProviderEnvironmentVariable? EnvironmentVariable,
    ModelProviderCredentialState CredentialState,
    string? CredentialMessage,
    bool RequiresRestart);

public sealed record ModelProviderSettingsSnapshot(
    IReadOnlyList<ModelProviderSettingsView> Providers);

public sealed record ModelProviderSettingsUpdate(
    ModelProviderName Provider,
    ModelProviderEndpoint Endpoint,
    AgentModel ChatModel,
    EmbeddingModel EmbeddingModel,
    EmbeddingDimensions EmbeddingDimensions,
    AgentContextTokenLimit? MaximumAgentContextTokens,
    ProviderTimeoutSeconds ConnectTimeout,
    ProviderTimeoutSeconds RequestTimeout,
    ModelProviderSecretName? SecretName,
    ModelProviderEnvironmentVariable? EnvironmentVariable);

public sealed record ModelProviderCredentialUpdate(
    ModelProviderName Provider,
    ModelProviderCredential Credential);

public sealed record ModelProviderSettingsResult(
    ModelProviderSettingsSnapshot? Snapshot,
    string? ErrorCode,
    string? Error);

public interface IModelProviderSettingsService
{
    ValueTask<ModelProviderSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ModelProviderSettingsResult> UpdateAsync(
        ModelProviderSettingsUpdate request,
        CancellationToken cancellationToken = default);

    ValueTask<ModelProviderSettingsResult> SetCredentialAsync(
        ModelProviderCredentialUpdate request,
        CancellationToken cancellationToken = default);
}
