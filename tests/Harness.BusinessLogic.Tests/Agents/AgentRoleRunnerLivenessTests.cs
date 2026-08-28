using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;
using Harness.DataAccess.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed partial class AgentRoleRunnerTests
{
    [Fact]
    public async Task Gives_implementer_one_in_session_correction_when_it_narrates_without_tools()
    {
        NarratingThenToolCallingModelProvider provider = new();
        CapturingAgentToolFactory tools = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            tools,
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"), AgentRole.Implementer, new("implement"), [new("src")]));

        Assert.Null(result.Error);
        Assert.Equal("finished after correction tool", result.Output?.Value);
        Assert.Equal("src/Program.cs", tools.RelativePath);
        Assert.Equal(3, provider.Requests.Count);
        Assert.Contains("TOOL EXECUTION REQUIRED",
            provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("BOUNDED TASK", provider.Requests[1].Messages[^1].Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uses_durable_tool_evidence_when_implementer_final_text_is_empty()
    {
        ToolCallingModelProvider provider = new(emptyFinalResponse: true);
        CapturingAgentToolFactory tools = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            tools,
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"), AgentRole.Implementer, new("inspect"), [new("src")]));

        Assert.Null(result.Error);
        Assert.Contains("durable tool evidence", result.Output?.Value,
            StringComparison.Ordinal);
        Assert.Equal("updated source", tools.EditedContent);
    }

    [Fact]
    public async Task Rejects_empty_role_output_without_a_tool_handoff()
    {
        AgentRoleRunner runner = CreateRunner(
            new CapturingModelProvider("  "),
            new CapturingModelProvider("unused"),
            new CapturingModelProvider("unused"));

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-1"), AgentRole.Lead, new("plan")));

        Assert.Equal("empty_agent_response", result.ErrorCode?.Value);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Dotted_directory_grant_falls_back_when_exact_file_read_is_missing()
    {
        CapturingModelProvider provider = new("implementer result");
        CapturingAgentToolFactory tools = new()
        {
            ReturnWorkspaceFileView = true,
            WorkspaceFileErrorCode = "file_missing",
        };
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            tools,
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"), AgentRole.Implementer,
            new("replace the existing test file"), [new("tests/TicTacToe.Tests")]));

        Assert.Null(result.Error);
        Assert.Equal("implementer result", result.Output?.Value);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal("read_file", Assert.Single(provider.Requests[0].Tools!).Name.Value);
    }

    [Fact]
    public async Task Reviewer_text_only_response_is_retried_with_inspection_tools()
    {
        ReviewerInspectionProvider provider = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "review-model", provider)),
            new ReviewerInspectionToolFactory(),
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"), AgentRole.Reviewer, new("review")));

        Assert.Null(result.Error);
        Assert.Contains("accept", result.Output?.Value, StringComparison.Ordinal);
        Assert.Equal(3, provider.Requests.Count);
        Assert.Contains("INDEPENDENT TOOL INSPECTION REQUIRED",
            provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reviewer_is_rejected_when_correction_still_skips_required_evidence()
    {
        ReviewerInspectionProvider provider = new(skipEvidenceTool: true);
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "review-model", provider)),
            new ReviewerInspectionToolFactory(),
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"), AgentRole.Reviewer, new("review")));

        Assert.Equal("reviewer_evidence_missing", result.ErrorCode?.Value);
        Assert.Null(result.Output);
    }

    private sealed class ReviewerInspectionToolFactory : IAgentToolFactory
    {
        public IList<AITool> Create(
            AgentRole role,
            GoalId goalId,
            IReadOnlyList<AgentFileArea> fileAreas,
            ModelAccess modelAccess) =>
        [
            AIFunctionFactory.Create(() => "diff", new() { Name = "inspect_git" }),
            AIFunctionFactory.Create(() => "evidence", new() { Name = "list_tool_evidence" }),
        ];
    }

    private sealed class ReviewerInspectionProvider(bool skipEvidenceTool = false) : IModelProvider
    {
        internal List<ChatRequest> Requests { get; } = [];

        public ValueTask<ModelCatalog> GetModelsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            await Task.Yield();
            if (Requests.Count == 1)
            {
                yield return new("{\"decision\":\"revise\",\"summary\":\"Need evidence.\"}",
                    string.Empty, true, "stop", new(3, 2), Error: null);
                yield break;
            }

            if (Requests.Count == 2)
            {
                yield return new(string.Empty, string.Empty, true, "tool_calls", new(4, 2),
                    Error: null,
                    skipEvidenceTool
                        ? [new(new("review-git"), new("inspect_git"), new("{}"))]
                        :
                        [
                            new(new("review-git"), new("inspect_git"), new("{}")),
                            new(new("review-evidence"), new("list_tool_evidence"), new("{}")),
                        ]);
                yield break;
            }

            yield return new("{\"decision\":\"accept\",\"summary\":\"Diff inspected.\"}",
                string.Empty, true, "stop", new(5, 2), Error: null);
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
