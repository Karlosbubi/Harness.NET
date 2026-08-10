using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Agents;
using Harness.DataAccess.Models;
using Microsoft.Extensions.AI;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class ModelProviderChatClientTests
{
    [Fact]
    public async Task Preserves_reasoning_and_tool_identity_across_the_tool_loop()
    {
        CapturingReasoningProvider provider = new();
        ModelProviderChatClient client = new(
            provider,
            new("reasoning-model"),
            remoteGoalId: null,
            AgentRole.Reviewer);
        Microsoft.Extensions.AI.ChatMessage user = new(AIChatRole.User, "Inspect the source.");

        List<AIContent> assistantContents = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync([user]))
        {
            assistantContents.AddRange(update.Contents);
        }

        Microsoft.Extensions.AI.ChatMessage assistant = new(
            AIChatRole.Assistant,
            assistantContents);
        Microsoft.Extensions.AI.ChatMessage tool = new(
            AIChatRole.Tool,
            [new FunctionResultContent("call-1", new { content = "source" })]);
        _ = await client.GetResponseAsync([user, assistant, tool]);

        Assert.Equal(2, provider.Requests.Count);
        Harness.DataAccess.Models.ChatMessage reasoningMessage = Assert.Single(
            provider.Requests[1].Messages,
            message => message.Reasoning is not null);
        Assert.Equal("I need the source.", reasoningMessage.Reasoning?.Text.Value);
        Assert.Contains("reasoning.text", reasoningMessage.Reasoning?.Details?.Value,
            StringComparison.Ordinal);
        ChatToolResult result = Assert.Single(provider.Requests[1].Messages,
            message => message.ToolResult is not null).ToolResult!;
        Assert.Equal("read_file", result.ToolName?.Value);
    }

    private sealed class CapturingReasoningProvider : IModelProvider
    {
        internal List<ChatRequest> Requests { get; } = [];

        public ValueTask<ModelCatalog> GetModelsAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (Requests.Count == 1)
            {
                yield return new(
                    string.Empty,
                    "I need the source.",
                    Done: true,
                    DoneReason: "tool_calls",
                    new(4, 2),
                    Error: null,
                    [new(new("call-1"), new("read_file"), new("{}"))],
                    new("[{\"type\":\"reasoning.text\",\"text\":\"private\"}]"));
                yield break;
            }

            yield return new("done", string.Empty, true, "stop", new(8, 3), Error: null);
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
