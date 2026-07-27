namespace Harness.Host.Configuration;

internal readonly record struct HarnessConfiguration(
    IReadOnlyDictionary<string, ModelProviderConfiguration> Providers,
    ProviderRoutingConfiguration Routing,
    ConversationConfiguration Conversation,
    ObservabilityConfiguration Observability,
    FrameworkConfiguration Framework);

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

internal readonly record struct FrameworkConfiguration(
    IReadOnlyList<FrameworkRuleConfiguration> Rules);

internal readonly record struct FrameworkRuleConfiguration(
    string Key,
    string Value,
    int Precedence,
    string Layer,
    bool IsLocked,
    string Source);
