using System.Runtime.CompilerServices;
using System.Text.Json;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Models;
using Microsoft.Extensions.AI;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using ProviderChatMessage = Harness.DataAccess.Models.ChatMessage;
using ProviderChatRole = Harness.DataAccess.Models.ChatRole;
using ProviderChatResponseFormat = Harness.DataAccess.Models.ChatResponseFormat;

namespace Harness.BusinessLogic.Agents;

internal sealed class ModelProviderChatClient(
    IModelProvider provider,
    AgentModel model,
    GoalId? remoteGoalId,
    AgentRole role,
    bool implementerInspectionBootstrapped = false,
    bool structuredLocalFileEditProposal = false) : IChatClient
{
    private int toolCallCount;
    private const string LeadResponseSchema = """
        {
          "type":"object",
          "additionalProperties":false,
          "required":["plan","tasks"],
          "properties":{
            "plan":{"type":"string","minLength":1},
            "tasks":{
              "type":"array","minItems":1,
              "items":{
                "type":"object","additionalProperties":false,
                "required":["title","objective","fileAreas","acceptanceCriteria"],
                "properties":{
                  "title":{"type":"string","minLength":1},
                  "objective":{"type":"string","minLength":1},
                  "fileAreas":{"type":"array","minItems":1,"items":{"type":"string","minLength":1}},
                  "acceptanceCriteria":{"type":"array","minItems":1,"items":{"type":"string","minLength":1}}
                }
              }
            }
          }
        }
        """;
    private const string ReviewerResponseSchema = """
        {
          "type":"object",
          "additionalProperties":false,
          "required":["decision","summary"],
          "properties":{
            "decision":{"type":"string","enum":["accept","revise"]},
            "summary":{"type":"string","minLength":1}
          }
        }
        """;
    private const string ImplementerHandoffSchema = """
        {
          "type":"object",
          "additionalProperties":false,
          "required":["status","summary","validation","remaining"],
          "properties":{
            "status":{"type":"string","enum":["complete","partial"]},
            "summary":{"type":"string","minLength":1},
            "validation":{"type":"array","items":{"type":"string"}},
            "remaining":{"type":"array","items":{"type":"string"}}
          }
        }
        """;
    internal int ToolCallCount => Volatile.Read(ref toolCallCount);

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
        List<Microsoft.Extensions.AI.ChatMessage> inputMessages = messages.ToList();
        bool followsToolResult = inputMessages.Any(message =>
            message.Contents.OfType<FunctionResultContent>().Any());
        HashSet<string> completedCallIds = inputMessages
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.CallId)
            .ToHashSet(StringComparer.Ordinal);
        bool followsCompletionTool = inputMessages
            .SelectMany(message => message.Contents.OfType<FunctionCallContent>())
            .Where(call => completedCallIds.Contains(call.CallId))
            .Any(call => call.Name is "apply_file_edit" or "apply_symbol_rename" or
                "dotnet_build" or "dotnet_test");
        List<ProviderChatMessage> providerMessages = [];
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            providerMessages.Add(new(ProviderChatRole.System, options.Instructions));
        }

        providerMessages.AddRange(inputMessages.SelectMany(MapMessage));

        IEnumerable<AITool> offeredTools = options?.Tools ?? [];
        if (structuredLocalFileEditProposal)
        {
            offeredTools = [];
        }
        else if (role is AgentRole.Implementer && !followsCompletionTool)
        {
            offeredTools = !implementerInspectionBootstrapped && !followsToolResult
                ? offeredTools
                    .OfType<AIFunctionDeclaration>()
                    .Where(tool => tool.Name == "read_file")
                : offeredTools
                    .OfType<AIFunctionDeclaration>()
                    .Where(tool => tool.Name is "read_file" or
                        "inspect_code_problems" or "get_symbol_info" or
                        "find_symbol_definition" or "find_symbol_references" or
                        "find_symbol_implementations" or "apply_file_edit" or
                        "preview_symbol_rename" or "apply_symbol_rename");
        }

        IReadOnlyList<ChatToolDefinition> tools = offeredTools
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
                tools,
                remoteGoalId is null &&
                    (role is AgentRole.Lead or AgentRole.Reviewer ||
                     role is AgentRole.Implementer && followsCompletionTool)
                    ? ProviderChatResponseFormat.Json
                    : ProviderChatResponseFormat.Text,
                remoteGoalId is null
                    ? role switch
                    {
                        AgentRole.Lead => new(LeadResponseSchema),
                        AgentRole.Reviewer => new(ReviewerResponseSchema),
                        AgentRole.Implementer when followsCompletionTool =>
                            new(ImplementerHandoffSchema),
                        _ => null,
                    }
                    : null,
                remoteGoalId is null ? 0 : null,
                structuredLocalFileEditProposal
                    ? ModelThinkingMode.Disabled
                    : ModelThinkingMode.ProviderDefault),
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
                Interlocked.Add(ref toolCallCount, item.ToolCalls.Count);
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
