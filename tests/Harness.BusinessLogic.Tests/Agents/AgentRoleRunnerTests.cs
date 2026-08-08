using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
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
            new("  bounded task  "),
            FileAreas: role is AgentRole.Implementer ? [new("src")] : null));

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
        Assert.Equal(Harness.DataAccess.Models.ChatRole.System, request.Messages[0].Role);
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
    public async Task Rejects_implementer_execution_without_a_bounded_file_area()
    {
        CapturingModelProvider implementer = new("unused");
        AgentRoleRunner runner = CreateRunner(
            new CapturingModelProvider("unused"),
            implementer,
            new CapturingModelProvider("unused"));

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-1"), AgentRole.Implementer, new("implement")));

        Assert.Equal("invalid_agent_request", result.ErrorCode?.Value);
        Assert.Empty(implementer.Requests);
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
    public async Task Binds_remote_execution_to_the_goal_without_a_user_token_ceiling()
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
            new EmptyAgentToolFactory(),
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-1"),
            AgentRole.Lead,
            new("plan")));

        Assert.Null(result.Error);
        ChatRequest request = Assert.Single(provider.Requests);
        Assert.Equal("goal-1", request.RemoteScope?.GoalId);
        Assert.Equal(
            ProviderPrivacyPolicy.NoCollectionAndZeroDataRetention,
            request.RemoteScope?.PrivacyPolicy);
        Assert.Equal(RemoteModelRole.Lead, request.RemoteScope?.Role);
    }

    [Fact]
    public async Task Invokes_provider_tool_calls_and_returns_the_result_to_the_model()
    {
        ToolCallingModelProvider provider = new();
        CapturingAgentToolFactory tools = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            tools,
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"),
            AgentRole.Lead,
            new("inspect")));

        Assert.Null(result.Error);
        Assert.Equal("finished after tool", result.Output?.Value);
        Assert.Equal("src/Program.cs", tools.RelativePath);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal("read_file", Assert.Single(provider.Requests[0].Tools!).Name.Value);
        ChatToolResult returned = Assert.Single(
            provider.Requests[1].Messages,
            message => message.ToolResult is not null)
            .ToolResult!;
        Assert.Equal("call-1", returned.CallId.Value);
        Assert.Contains("bounded file", returned.Result.Value, StringComparison.Ordinal);
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
        new EmptyAgentToolFactory(),
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

    private sealed class EmptyAgentToolFactory : IAgentToolFactory
    {
        public IList<AITool> Create(
            AgentRole role,
            GoalId goalId,
            IReadOnlyList<AgentFileArea> fileAreas) => [];
    }

    private sealed class CapturingAgentToolFactory : IAgentToolFactory
    {
        internal string? RelativePath { get; private set; }

        public IList<AITool> Create(
            AgentRole role,
            GoalId goalId,
            IReadOnlyList<AgentFileArea> fileAreas) =>
        [
            AIFunctionFactory.Create(
                (string relativePath) =>
                {
                    RelativePath = relativePath;
                    return "bounded file";
                },
                new()
                {
                    Name = "read_file",
                    Description = "Read a bounded file.",
                }),
        ];
    }

    private sealed class ToolCallingModelProvider : IModelProvider
    {
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
            if (Requests.Count == 1)
            {
                yield return new(
                    string.Empty,
                    string.Empty,
                    Done: true,
                    DoneReason: "tool_calls",
                    new(4, 1),
                    Error: null,
                    [new(new("call-1"), new("read_file"),
                        new("{\"relativePath\":\"src/Program.cs\"}"))]);
                yield break;
            }

            yield return new(
                "finished after tool",
                string.Empty,
                Done: true,
                DoneReason: "stop",
                new(8, 4),
                Error: null);
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
