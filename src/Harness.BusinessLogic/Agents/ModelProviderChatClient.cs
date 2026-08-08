using System.Runtime.CompilerServices;
using System.Text.Json;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Models;
using Microsoft.Extensions.AI;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using ProviderChatMessage = Harness.DataAccess.Models.ChatMessage;
using ProviderChatRole = Harness.DataAccess.Models.ChatRole;

namespace Harness.BusinessLogic.Agents;

internal sealed class ModelProviderChatClient(
    IModelProvider provider,
    AgentModel model,
    GoalId? remoteGoalId,
    AgentRole role) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in GetStreamingResponseAsync(
            messages,
            options,
            cancellationToken))
        {
            updates.Add(update);
        }

        List<AIContent> contents = updates
            .SelectMany(update => update.Contents)
            .ToList();
        return new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(
            AIChatRole.Assistant,
            contents));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ProviderChatMessage> providerMessages = [];
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            providerMessages.Add(new(ProviderChatRole.System, options.Instructions));
        }

        providerMessages.AddRange(messages.SelectMany(MapMessage));

        IReadOnlyList<ChatToolDefinition> tools = options?.Tools?
            .OfType<AIFunctionDeclaration>()
            .Select(tool => new ChatToolDefinition(
                new(tool.Name),
                new(tool.Description ?? string.Empty),
                new(tool.JsonSchema.GetRawText())))
            .ToArray() ?? [];

        await foreach (ChatStreamEvent item in provider.StreamChatAsync(
            new ChatRequest(
                model.Value,
                providerMessages,
                remoteGoalId is null
                    ? null
                    : new(
                        remoteGoalId.Value,
                        ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention,
                        role switch
                        {
                            AgentRole.Lead => RemoteModelRole.Lead,
                            AgentRole.Implementer => RemoteModelRole.Implementer,
                            AgentRole.Reviewer => RemoteModelRole.Reviewer,
                            _ => throw new ArgumentOutOfRangeException(nameof(role)),
                        }),
                tools),
            cancellationToken))
        {
            if (item.Error is not null)
            {
                throw new InvalidOperationException(
                    $"Model provider error '{item.Error.Code}': {item.Error.Message}");
            }

            List<AIContent> contents = [];
            if (!string.IsNullOrEmpty(item.Content))
            {
                contents.Add(new TextContent(item.Content));
            }

            if (item.ToolCalls is not null)
            {
                contents.AddRange(item.ToolCalls.Select(call => new FunctionCallContent(
                    call.Id.Value,
                    call.Name.Value,
                    DeserializeArguments(call.Arguments))));
            }

            if (contents.Count > 0)
            {
                yield return new(AIChatRole.Assistant, contents);
            }
        }
    }

    private static IEnumerable<ProviderChatMessage> MapMessage(
        Microsoft.Extensions.AI.ChatMessage message)
    {
        ProviderChatRole role = message.Role == AIChatRole.System
            ? ProviderChatRole.System
            : message.Role == AIChatRole.User
                ? ProviderChatRole.User
                : message.Role == AIChatRole.Assistant
                    ? ProviderChatRole.Assistant
                    : ProviderChatRole.Tool;
        ChatToolCall[] calls = message.Contents
            .OfType<FunctionCallContent>()
            .Select(call => new ChatToolCall(
                new(call.CallId),
                new(call.Name),
                new(JsonSerializer.Serialize(call.Arguments))))
            .ToArray();
        FunctionResultContent[] results = message.Contents
            .OfType<FunctionResultContent>()
            .ToArray();

        if (results.Length == 0)
        {
            yield return new(
                role,
                message.Text,
                calls.Length == 0 ? null : calls,
                ToolResult: null);
            yield break;
        }

        foreach (FunctionResultContent result in results)
        {
            yield return new(
                ProviderChatRole.Tool,
                Content: string.Empty,
                ToolCalls: null,
                new(new(result.CallId), new(JsonSerializer.Serialize(result.Result))));
        }
    }

    private static IDictionary<string, object?> DeserializeArguments(ChatToolArgumentsJson arguments)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.Value) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The provider returned invalid tool arguments.", exception);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }
}
