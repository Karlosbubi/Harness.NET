using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Models;
using Microsoft.Extensions.AI;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class AgentActivityServiceTests
{
    [Fact]
    public void Broken_observer_cannot_interrupt_agent_activity_lifecycle()
    {
        AgentActivityService activities = new(TimeProvider.System);
        activities.Changed += () => throw new InvalidOperationException("presentation failed");

        using (AgentActivityLease activity = activities.BeginTool(
                   new("goal-activity"), AgentRole.Lead, "read_file"))
        {
            Assert.Single(activities.GetSnapshot().Items);
            activity.Complete();
        }

        Assert.Equal(
            AgentActivityPhase.Completed,
            Assert.Single(activities.GetSnapshot().Items).Phase);
    }

    [Fact]
    public async Task Provider_lifecycle_exposes_only_sanitized_state_and_records_completion()
    {
        MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-28T19:00:00Z"));
        WaitingProvider provider = new();
        AgentActivityService activities = new(time);
        ModelProviderChatClient client = new(
            provider,
            new("local-model"),
            remoteGoalId: null,
            AgentRole.Implementer,
            activityGoalId: new("goal-activity"),
            activityService: activities);
        await using IAsyncEnumerator<ChatResponseUpdate> stream = client
            .GetStreamingResponseAsync([
                new Microsoft.Extensions.AI.ChatMessage(AIChatRole.User, "private prompt"),
            ])
            .GetAsyncEnumerator();

        Task<bool> first = stream.MoveNextAsync().AsTask();
        await provider.Started.Task;
        AgentActivityView waiting = Assert.Single(activities.GetSnapshot().Items);
        Assert.Equal(AgentActivityPhase.WaitingForResponse, waiting.Phase);
        Assert.Equal("model_response", waiting.Operation.Value);
        Assert.DoesNotContain("private", waiting.ToString(), StringComparison.Ordinal);

        time.Advance(TimeSpan.FromSeconds(3));
        provider.Release.SetResult();
        Assert.True(await first);
        AgentActivityView receiving = Assert.Single(activities.GetSnapshot().Items);
        Assert.Equal(AgentActivityPhase.ReceivingResponse, receiving.Phase);
        Assert.Equal(time.GetUtcNow(), receiving.UpdatedAt);

        Assert.False(await stream.MoveNextAsync());
        Assert.Equal(
            AgentActivityPhase.Completed,
            Assert.Single(activities.GetSnapshot().Items).Phase);
    }

    [Fact]
    public void Completed_activity_history_is_bounded()
    {
        AgentActivityService activities = new(TimeProvider.System);

        for (int index = 0; index < 40; index++)
        {
            using AgentActivityLease activity = activities.BeginTool(
                new("goal-activity"), AgentRole.Lead, $"tool_{index}");
            activity.Complete();
        }

        Assert.Equal(32, activities.GetSnapshot().Items.Count);
    }

    [Fact]
    public async Task Tool_wrapper_tracks_read_only_invocation_and_preserves_function_contract()
    {
        MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-28T19:00:00Z"));
        AgentActivityService activities = new(time);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AIFunction inner = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                await release.Task.WaitAsync(cancellationToken);
                return "bounded source";
            },
            new() { Name = "read_file", Description = "Read one file." });
        ObservedAgentFunction observed = new(
            inner, activities, new GoalId("goal-activity"), AgentRole.Implementer);

        Task<object?> invocation = observed.InvokeAsync(new AIFunctionArguments()).AsTask();
        AgentActivityView running = Assert.Single(activities.GetSnapshot().Items);
        Assert.Equal(AgentActivityKind.ToolInvocation, running.Kind);
        Assert.Equal("read_file", running.Operation.Value);
        Assert.Equal(inner.JsonSchema.GetRawText(), observed.JsonSchema.GetRawText());

        release.SetResult();
        _ = await invocation;
        Assert.Equal(
            AgentActivityPhase.Completed,
            Assert.Single(activities.GetSnapshot().Items).Phase);
    }

    [Fact]
    public async Task Tool_wrapper_records_failure_without_retaining_exception_content()
    {
        AgentActivityService activities = new(TimeProvider.System);
        AIFunction inner = AIFunctionFactory.Create(
            (Func<string>)(() =>
                throw new InvalidOperationException("private failure payload")),
            new() { Name = "inspect_git" });
        ObservedAgentFunction observed = new(
            inner, activities, new("goal-activity"), AgentRole.Reviewer);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await observed.InvokeAsync(new AIFunctionArguments()));

        Assert.Contains("private failure", error.Message, StringComparison.Ordinal);
        AgentActivityView failed = Assert.Single(activities.GetSnapshot().Items);
        Assert.Equal(AgentActivityPhase.Failed, failed.Phase);
        Assert.DoesNotContain("private failure", failed.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tool_wrapper_records_cancellation()
    {
        AgentActivityService activities = new(TimeProvider.System);
        AIFunction inner = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
            new() { Name = "search_text" });
        ObservedAgentFunction observed = new(
            inner, activities, new("goal-activity"), AgentRole.Lead);
        using CancellationTokenSource cancellation = new();

        Task<object?> invocation = observed.InvokeAsync(
            new AIFunctionArguments(), cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await invocation);

        Assert.Equal(
            AgentActivityPhase.Cancelled,
            Assert.Single(activities.GetSnapshot().Items).Phase);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset value = now;

        public override DateTimeOffset GetUtcNow() => value;

        internal void Advance(TimeSpan duration) => value += duration;
    }

    private sealed class WaitingProvider : IModelProvider
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ModelCatalog> GetModelsAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            yield return new("done", string.Empty, true, "stop", new(1, 1), Error: null);
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
