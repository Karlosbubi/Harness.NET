using Harness.BusinessLogic.Agents;
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

        AgentRunResult result = await runner.RunAsync(new(role, new("  bounded task  ")));

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
    }

    [Fact]
    public async Task Rejects_an_empty_task_without_calling_a_provider()
    {
        CapturingModelProvider lead = new("unused");
        AgentRoleRunner runner = CreateRunner(
            lead,
            new CapturingModelProvider("unused"),
            new CapturingModelProvider("unused"));

        AgentRunResult result = await runner.RunAsync(new(AgentRole.Lead, new("  ")));

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

        AgentRunResult result = await runner.RunAsync(new(AgentRole.Lead, new("plan")));

        Assert.Equal("agent_run_failed", result.ErrorCode?.Value);
        Assert.Contains("provider_failed", result.Error?.Value, StringComparison.Ordinal);
        Assert.Null(result.Output);
    }

    [Fact]
    public void Requires_a_registration_for_every_role()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new AgentRoleRunner(
            [new(AgentRole.Lead, new("model"), new CapturingModelProvider("unused"))],
            NullLoggerFactory.Instance));

        Assert.Contains("Implementer", error.Message, StringComparison.Ordinal);
        Assert.Contains("Reviewer", error.Message, StringComparison.Ordinal);
    }

    private static AgentRoleRunner CreateRunner(
        IModelProvider lead,
        IModelProvider implementer,
        IModelProvider reviewer) => new(
        [
            new(AgentRole.Lead, new("lead-model"), lead),
            new(AgentRole.Implementer, new("implementer-model"), implementer),
            new(AgentRole.Reviewer, new("reviewer-model"), reviewer),
        ],
        NullLoggerFactory.Instance);

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
