using Harness.DataAccess.Models.Configuration;
using Harness.DataAccess.Secrets;

namespace Harness.BusinessLogic.Agents;

internal sealed class ModelProviderSettingsService(
    IModelProviderConfigurationStore configurationStore,
    ISecretStore secretStore) : IModelProviderSettingsService
{
    private const int MaximumEmbeddingDimensions = 65_536;
    private const int MaximumTimeoutSeconds = 3_600;
    private const int MaximumCredentialLength = 16_384;

    public async ValueTask<ModelProviderSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredModelProviderConfiguration> providers =
            await configurationStore.ListAsync(cancellationToken);
        List<ModelProviderSettingsView> views = [];
        foreach (StoredModelProviderConfiguration provider in providers)
        {
            views.Add(await MapAsync(provider, cancellationToken));
        }

        return new(views);
    }

    public async ValueTask<ModelProviderSettingsResult> UpdateAsync(
        ModelProviderSettingsUpdate request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.Provider is null ||
            string.IsNullOrWhiteSpace(request.Provider.Value))
        {
            return Failure("invalid_provider_configuration", "A configured provider is required.");
        }

        StoredModelProviderConfiguration? current = (await configurationStore.ListAsync(
                cancellationToken))
            .SingleOrDefault(provider => provider.Name.Value.Equals(
                request.Provider.Value,
                StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            return Failure("provider_missing", $"Provider '{request.Provider.Value}' is not configured.");
        }

        string? validation = Validate(request, current.Kind);
        if (validation is not null)
        {
            return Failure("invalid_provider_configuration", validation);
        }

        Uri endpoint = new(request.Endpoint.Value, UriKind.Absolute);
        SecretReference? secret = current.Kind is StoredModelProviderKind.OpenRouter
            ? new(
                request.SecretName!.Value,
                string.IsNullOrWhiteSpace(request.EnvironmentVariable?.Value)
                    ? null
                    : request.EnvironmentVariable.Value.Trim())
            : null;
        await configurationStore.SaveAsync(new(
            current.Name,
            current.Kind,
            new(endpoint),
            new(request.ChatModel.Value.Trim()),
            new(request.EmbeddingModel.Value.Trim()),
            new(request.EmbeddingDimensions.Value),
            new(TimeSpan.FromSeconds(request.ConnectTimeout.Value)),
            new(TimeSpan.FromSeconds(request.RequestTimeout.Value)),
            secret,
            RequiresRestart: true), cancellationToken);
        return new(await GetAsync(cancellationToken), ErrorCode: null, Error: null);
    }

    public async ValueTask<ModelProviderSettingsResult> SetCredentialAsync(
        ModelProviderCredentialUpdate request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.Provider is null ||
            string.IsNullOrWhiteSpace(request.Provider.Value) ||
            request.Credential is null || string.IsNullOrWhiteSpace(request.Credential.Value) ||
            request.Credential.Value.Length > MaximumCredentialLength)
        {
            return Failure(
                "invalid_provider_credential",
                $"A non-empty provider credential of at most {MaximumCredentialLength} characters is required.");
        }

        StoredModelProviderConfiguration? provider = (await configurationStore.ListAsync(
                cancellationToken))
            .SingleOrDefault(candidate => candidate.Name.Value.Equals(
                request.Provider.Value,
                StringComparison.OrdinalIgnoreCase));
        if (provider?.Kind is not StoredModelProviderKind.OpenRouter ||
            provider.ApiKeyReference is null)
        {
            return Failure(
                "provider_credential_unsupported",
                $"Provider '{request.Provider.Value}' does not accept an API credential.");
        }

        await secretStore.SetAsync(
            provider.ApiKeyReference,
            request.Credential.Value.Trim(),
            cancellationToken);
        return new(await GetAsync(cancellationToken), ErrorCode: null, Error: null);
    }

    private async ValueTask<ModelProviderSettingsView> MapAsync(
        StoredModelProviderConfiguration provider,
        CancellationToken cancellationToken)
    {
        ModelProviderCredentialState credentialState = ModelProviderCredentialState.NotApplicable;
        string? credentialMessage = null;
        if (provider.ApiKeyReference is { } secret)
        {
            try
            {
                string? credential = await secretStore.GetAsync(secret, cancellationToken);
                credentialState = string.IsNullOrWhiteSpace(credential)
                    ? ModelProviderCredentialState.Missing
                    : ModelProviderCredentialState.Configured;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                credentialState = ModelProviderCredentialState.Unavailable;
                credentialMessage = exception.Message;
            }
        }

        return new(
            new(provider.Name.Value),
            provider.Kind is StoredModelProviderKind.Ollama
                ? AgentModelProviderKind.Ollama
                : AgentModelProviderKind.OpenRouter,
            new(provider.Endpoint.Value.AbsoluteUri.TrimEnd('/')),
            new(provider.ChatModel.Value),
            new(provider.EmbeddingModel.Value),
            new(provider.EmbeddingDimensions.Value),
            new(checked((int)provider.ConnectTimeout.Value.TotalSeconds)),
            new(checked((int)provider.RequestTimeout.Value.TotalSeconds)),
            provider.ApiKeyReference is null ? null : new(provider.ApiKeyReference.Name),
            provider.ApiKeyReference?.EnvironmentVariable is null
                ? null
                : new(provider.ApiKeyReference.EnvironmentVariable),
            credentialState,
            credentialMessage,
            provider.RequiresRestart);
    }

    private static string? Validate(
        ModelProviderSettingsUpdate request,
        StoredModelProviderKind kind)
    {
        if (!Uri.TryCreate(request.Endpoint?.Value, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            return "The provider endpoint must be an absolute HTTP or HTTPS URI.";
        }

        if (kind is StoredModelProviderKind.OpenRouter &&
            endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback)
        {
            return "OpenRouter credentials require HTTPS except for a loopback endpoint.";
        }

        if (request.ChatModel is null || string.IsNullOrWhiteSpace(request.ChatModel.Value) ||
            request.EmbeddingModel is null || string.IsNullOrWhiteSpace(request.EmbeddingModel.Value))
        {
            return "Chat and embedding default models are required.";
        }

        if (request.EmbeddingDimensions is null ||
            request.EmbeddingDimensions.Value is < 1 or > MaximumEmbeddingDimensions)
        {
            return $"Embedding dimensions must be from 1 through {MaximumEmbeddingDimensions}.";
        }

        if (request.ConnectTimeout is null ||
            request.ConnectTimeout.Value is < 1 or > MaximumTimeoutSeconds ||
            request.RequestTimeout is null ||
            request.RequestTimeout.Value is < 1 or > MaximumTimeoutSeconds)
        {
            return $"Provider timeouts must be from 1 through {MaximumTimeoutSeconds} seconds.";
        }

        if (kind is StoredModelProviderKind.OpenRouter &&
            (request.SecretName is null || string.IsNullOrWhiteSpace(request.SecretName.Value)))
        {
            return "An OpenRouter Secret Service key name is required.";
        }

        return null;
    }

    private static ModelProviderSettingsResult Failure(string code, string error) =>
        new(null, code, error);
}
