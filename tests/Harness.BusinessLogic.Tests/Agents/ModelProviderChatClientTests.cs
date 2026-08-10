using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.VisualCapture;
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

    [Fact]
    public async Task Attaches_exact_capture_bytes_after_the_typed_tool_result()
    {
        CapturingReasoningProvider provider = new();
        ModelProviderChatClient client = new(
            provider, new("vision-model"), remoteGoalId: null, AgentRole.Reviewer);
        VisualCaptureView capture = new(
            new("11111111111111111111111111111111"), new GoalId("goal-a"), "workspace-a",
            VisualCaptureInitiator.Reviewer, new("Verify UI"), new("Harness.NET"),
            VisualCaptureTarget.Window, VisualCaptureIdentityState.Unavailable, null, null,
            VisualCaptureScaleState.ApplicationSupplied, new(2), new(10, 20),
            new("image/png"), new(3), new(new string('a', 64)), DateTimeOffset.UtcNow);
        Microsoft.Extensions.AI.ChatMessage tool = new(
            AIChatRole.Tool,
            [new FunctionResultContent("call-image", new VisualCaptureInspectionResult(
                VisualCaptureOutcome.Succeeded,
                new(capture, new("AQID")),
                null,
                null))]);

        _ = await client.GetResponseAsync([tool]);

        ChatRequest request = Assert.Single(provider.Requests);
        Harness.DataAccess.Models.ChatMessage image = Assert.Single(
            request.Messages, message => message.Image is not null);
        Assert.Equal("AQID", image.Image?.Base64.Value);
        Assert.Contains(capture.Sha256.Value, image.Content, StringComparison.Ordinal);
        ChatToolResult result = Assert.Single(request.Messages,
            message => message.ToolResult is not null).ToolResult!;
        Assert.DoesNotContain("AQID", result.Result.Value, StringComparison.Ordinal);
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
