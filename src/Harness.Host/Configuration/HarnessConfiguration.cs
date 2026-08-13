using Harness.DataAccess.Secrets;

namespace Harness.Host.Configuration;

internal readonly record struct HarnessConfiguration(
    IReadOnlyDictionary<string, ModelProviderConfiguration> Providers,
    IReadOnlyList<McpConnectionConfiguration> McpConnections,
    ProviderRoutingConfiguration Routing,
    ConversationConfiguration Conversation,
    ObservabilityConfiguration Observability,
    FrameworkConfiguration Framework);

internal readonly record struct McpConnectionConfiguration(
    string Name,
    Uri Endpoint,
    TimeSpan RequestTimeout,
    bool IsEnabled,
    McpConnectionAccessKind Access,
    string? ClientId,
    IReadOnlyList<string> AllowedTools);

internal enum McpConnectionAccessKind
{
    ReadOnly,
    HarnessControl,
}

internal readonly record struct ModelProviderConfiguration(
    string Name,
    ModelProviderKind Kind,
    Uri Endpoint,
    string ChatModel,
    string EmbeddingModel,
    int EmbeddingDimensions,
    TimeSpan ConnectTimeout,
    TimeSpan RequestTimeout,
    SecretReference? ApiKeyReference);

internal readonly record struct ProviderRoutingConfiguration(
    string MainLlm,
    string Reviewer,
    string ToolLlm,
    string Embedding);

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
