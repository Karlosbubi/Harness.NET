namespace Harness.Host.Configuration;

internal readonly record struct HarnessConfiguration(
    IReadOnlyDictionary<string, ModelProviderConfiguration> Providers,
    ProviderRoutingConfiguration Routing,
    ConversationConfiguration Conversation,
    ObservabilityConfiguration Observability);

internal readonly record struct ModelProviderConfiguration(
    string Name,
    string Kind,
    Uri Endpoint,
    string ChatModel,
    string EmbeddingModel,
    TimeSpan ConnectTimeout,
    TimeSpan RequestTimeout);

internal readonly record struct ProviderRoutingConfiguration(
    string MainLlm,
    string Reviewer,
    string ToolLlm);

internal readonly record struct ConversationConfiguration(
    string Id,
    string Title,
    string WorkspacePath);

internal readonly record struct ObservabilityConfiguration(Uri? OtlpEndpoint);
