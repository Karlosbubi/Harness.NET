using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class AgentRoleRunnerTests
{
    [Theory]
    [InlineData(AgentRole.Lead, "lead-model", "lead agent")]
    [InlineData(AgentRole.Implementer, "implementer-model", "implementer agent")]
    [InlineData(AgentRole.Reviewer, "reviewer-model", "reviewer agent")]
    public async Task Runs_each_role_through_its_registered_model_and_prompt(
        AgentRole role,
        string expectedModel,
        string expectedPrompt)
    {
        CapturingModelProvider lead = new("lead result");
        CapturingModelProvider implementer = new("implementer result");
        CapturingModelProvider reviewer = new("reviewer result");
        AgentRoleRunner runner = CreateRunner(lead, implementer, reviewer);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-1"),
            role,
            new("  bounded task  ")));

        Assert.Null(result.Error);
        Assert.Equal($"{role.ToString().ToLowerInvariant()} result", result.Output?.Value);
        CapturingModelProvider selected = role switch
        {
            AgentRole.Lead => lead,
            AgentRole.Implementer => implementer,
            AgentRole.Reviewer => reviewer,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        ChatRequest request = Assert.Single(selected.Requests);
        Assert.Equal(expectedModel, request.Model);
        Assert.Equal("system", request.Messages[0].Role);
        Assert.Contains(expectedPrompt, request.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("bounded task", request.Messages[^1].Content);
        Assert.Null(request.RemoteScope);
    }

    [Fact]
    public async Task Rejects_an_empty_task_without_calling_a_provider()
    {
        CapturingModelProvider lead = new("unused");
        AgentRoleRunner runner = CreateRunner(
            lead,
            new CapturingModelProvider("unused"),
            new CapturingModelProvider("unused"));

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-1"),
            AgentRole.Lead,
            new("  ")));

        Assert.Equal("invalid_agent_request", result.ErrorCode?.Value);
        Assert.Null(result.Output);
        Assert.Empty(lead.Requests);
    }

    [Fact]
    public async Task Converts_provider_failures_to_a_role_result()
    {
        CapturingModelProvider lead = new(
            new ProviderError("provider_failed", "Unavailable", true));
        AgentRoleRunner runner = CreateRunner(
            lead,
            new CapturingModelProvider("unused"),
            new CapturingModelProvider("unused"));

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-1"),
            AgentRole.Lead,
            new("plan")));

        Assert.Equal("agent_run_failed", result.ErrorCode?.Value);
        Assert.Contains("provider_failed", result.Error?.Value, StringComparison.Ordinal);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Binds_remote_execution_to_the_goal_with_a_strict_output_cap()
    {
        CapturingModelProvider provider = new("remote result");
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => new(
                new(
                    new("goal-1"),
                    role,
                    new("OpenRouter"),
                    new("remote-model"),
                    ModelAccess.Remote,
                    provider),
                ErrorCode: null,
                Error: null)),
            NullLoggerFactory.Instance);

        AgentRunResult missingCap = await runner.RunAsync(new(
            new("goal-1"),
            AgentRole.Lead,
            new("plan")));
        AgentRunResult result = await runner.RunAsync(new(
            new("goal-1"),
            AgentRole.Lead,
            new("plan"),
            new(512)));

        Assert.Equal("maximum_output_tokens_required", missingCap.ErrorCode?.Value);
        Assert.Null(result.Error);
        ChatRequest request = Assert.Single(provider.Requests);
        Assert.Equal("goal-1", request.RemoteScope?.GoalId);
        Assert.Equal(
            ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention,
            request.RemoteScope?.PrivacyPolicy);
        Assert.Equal(RemoteModelRole.Lead, request.RemoteScope?.Role);
        Assert.Equal(512, request.MaximumOutputTokens?.Value);
    }

    private static AgentRoleRunner CreateRunner(
        IModelProvider lead,
        IModelProvider implementer,
        IModelProvider reviewer) => new(
        new StubRouteResolver(role => role switch
        {
            AgentRole.Lead => Route(role, "lead-model", lead),
            AgentRole.Implementer => Route(role, "implementer-model", implementer),
            AgentRole.Reviewer => Route(role, "reviewer-model", reviewer),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        }),
        NullLoggerFactory.Instance);

    private static GoalModelRouteResult Route(
        AgentRole role,
        string model,
        IModelProvider provider) => new(
        new(
            new("goal-1"),
            role,
            new("Local"),
            new(model),
            ModelAccess.Local,
            provider),
        ErrorCode: null,
        Error: null);

    private sealed class StubRouteResolver(Func<AgentRole, GoalModelRouteResult> resolve)
        : IGoalModelRouteResolver
    {
        public ValueTask<GoalModelRouteResult> ResolveAsync(
            GoalId goalId,
            AgentRole role,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(resolve(role));
    }

    private sealed class CapturingModelProvider : IModelProvider
    {
        private readonly string? output;
        private readonly ProviderError? error;

        internal CapturingModelProvider(string output)
        {
            this.output = output;
        }

        internal CapturingModelProvider(ProviderError error)
        {
            this.error = error;
        }

        internal List<ChatRequest> Requests { get; } = [];

        public ValueTask<ModelCatalog> GetModelsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(
                output ?? string.Empty,
                Thinking: string.Empty,
                Done: true,
                DoneReason: "stop",
                new(0, 0),
                error);
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
