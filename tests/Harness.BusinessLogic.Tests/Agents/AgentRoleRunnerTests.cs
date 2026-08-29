using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.DataAccess.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ProviderChatResponseFormat = Harness.DataAccess.Models.ChatResponseFormat;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed partial class AgentRoleRunnerTests
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
        Assert.Contains("Roslyn", request.Messages[0].Content, StringComparison.Ordinal);
        if (role is AgentRole.Implementer)
        {
            Assert.Contains("first action must be a typed inspection tool call",
                request.Messages[0].Content, StringComparison.Ordinal);
            Assert.Contains("A final response before at least one successful mutation is a failed task",
                request.Messages[0].Content, StringComparison.Ordinal);
        }
        Assert.Equal("bounded task", request.Messages[^1].Content);
        Assert.Equal(
            role is AgentRole.Lead or AgentRole.Reviewer
                ? ProviderChatResponseFormat.Json
                : ProviderChatResponseFormat.Text,
            request.ResponseFormat);
        if (role is AgentRole.Lead or AgentRole.Reviewer)
        {
            Assert.NotNull(request.ResponseSchema);
            Assert.Contains(role is AgentRole.Lead ? "fileAreas" : "decision",
                request.ResponseSchema.Value, StringComparison.Ordinal);
            Assert.Contains("\"minLength\":1", request.ResponseSchema.Value,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(request.ResponseSchema);
        }
        Assert.Null(request.RemoteScope);
        Assert.Equal(0, request.Temperature);
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
                    ModelAccess.Remote, AgentReasoningPolicy.ProviderDefault,
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
            AgentRole.Implementer,
            new("inspect"),
            [new("src")]));

        Assert.Null(result.Error);
        Assert.Equal("finished after edit", result.Output?.Value);
        Assert.Equal("src/Program.cs", tools.RelativePath);
        Assert.Equal("updated source", tools.EditedContent);
        Assert.Equal(3, provider.Requests.Count);
        Assert.Equal("read_file", Assert.Single(provider.Requests[0].Tools!).Name.Value);
        ChatToolResult readResult = Assert.Single(
            provider.Requests[1].Messages,
            message => message.ToolResult is not null)
            .ToolResult!;
        Assert.Equal("call-1", readResult.CallId.Value);
        Assert.Contains("bounded file", readResult.Result.Value, StringComparison.Ordinal);
        Assert.Equal(ProviderChatResponseFormat.Text, provider.Requests[1].ResponseFormat);
        Assert.Contains(provider.Requests[1].Tools!, tool => tool.Name.Value == "apply_file_edit");
        Assert.Contains(provider.Requests[1].Tools!, tool => tool.Name.Value == "get_symbol_info");
        Assert.Contains(provider.Requests[1].Tools!,
            tool => tool.Name.Value == "find_symbol_definition");
        ChatToolResult editResult = Assert.Single(
            provider.Requests[2].Messages,
            message => message.ToolResult?.CallId.Value == "call-2")
            .ToolResult!;
        Assert.Contains("edit applied", editResult.Result.Value, StringComparison.Ordinal);
        Assert.Equal(ProviderChatResponseFormat.Json, provider.Requests[2].ResponseFormat);
        Assert.Contains("\"remaining\"", provider.Requests[2].ResponseSchema?.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bootstraps_exact_file_inspection_before_requesting_a_mutation()
    {
        BootstrapMutationModelProvider provider = new();
        CapturingAgentToolFactory tools = new() { ReturnWorkspaceFileView = true };
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            tools,
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"),
            AgentRole.Implementer,
            new("replace the exact source file"),
            [new("src/Program.cs")]));

        Assert.Null(result.Error);
        Assert.Equal("src/Program.cs", tools.RelativePath);
        Assert.Equal("bootstrapped source", tools.EditedContent);
        Assert.Single(provider.Requests);
        Assert.Empty(provider.Requests[0].Tools!);
        Assert.Equal(ProviderChatResponseFormat.Text, provider.Requests[0].ResponseFormat);
        Assert.Equal(ModelReasoningEffort.None, provider.Requests[0].ReasoningEffort);
        Assert.Null(provider.Requests[0].ResponseSchema);
        Assert.Contains("DETERMINISTIC TYPED INSPECTION", provider.Requests[0].Messages[^1].Content,
            StringComparison.Ordinal);
        Assert.Contains("bounded file", provider.Requests[0].Messages[^1].Content,
            StringComparison.Ordinal);
        Assert.Contains("Applied the typed edit", result.Output?.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Structured_local_test_edit_preserves_source_escapes_and_runs_build_and_test()
    {
        const string source = "Console.WriteLine(\"\\n\");";
        CapturingModelProvider provider = new($"```csharp\n{source}\n```");
        RecordingMutationService mutations = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            new CapturingAgentToolFactory { ReturnWorkspaceFileView = true },
            NullLoggerFactory.Instance,
            new StaticInspectionService(),
            mutations);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"),
            AgentRole.Implementer,
            new("replace the exact test file"),
            [new("tests/UnitTest1.cs")]));

        Assert.Null(result.Error);
        Assert.Equal(source, mutations.Content);
        Assert.Equal([DotNetOperation.Build, DotNetOperation.Test], mutations.Operations);
    }

    [Fact]
    public async Task Structured_local_repair_applies_a_small_exact_replacement()
    {
        RepairingModelProvider provider = new();
        RecordingMutationService mutations = new(
            testFailures: 1,
            failureOutput: "Failure at tests/Acceptance/ContractTests.cs:line 42");
        StaticInspectionService inspection = new("Console.WriteLine(\"old\");");
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            new CapturingAgentToolFactory { ReturnWorkspaceFileView = true },
            NullLoggerFactory.Instance,
            inspection,
            mutations);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"),
            AgentRole.Implementer,
            new("""
                FULL GOAL OBJECTIVE (AUTHORITATIVE)
                Preserve this authoritative contract during every correction.

                APPROVED PLAN
                A deliberately verbose plan that does not need repeating.

                DELEGATED TASK
                Repair the exact source file.
                """),
            [new("src/Program.cs")]));

        Assert.Null(result.Error);
        Assert.Equal("Console.WriteLine(\"good\");", mutations.Content);
        Assert.Equal(
            [DotNetOperation.Build, DotNetOperation.Test,
                DotNetOperation.Build, DotNetOperation.Test],
            mutations.Operations);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Contains("one to four exact replacement blocks",
            provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("REPAIR BASE SOURCE\nConsole.WriteLine(\"bad\");",
            provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("Preserve this authoritative contract",
            provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("deliberately verbose plan",
            provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("DETERMINISTIC CITED SOURCE (read-only): " +
            "tests/Acceptance/ContractTests.cs",
            provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("assertion expects a value but the cited target frame throws",
            provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("tests/Acceptance/ContractTests.cs", inspection.Paths);
    }

    [Fact]
    public async Task Structured_local_type_fragment_preserves_the_existing_namespace_and_sibling_types()
    {
        const string baseline = """
            namespace TicTacToe.Core;

            public enum Mark { Empty, X, O }

            public sealed class GameState { }
            """;
        CapturingModelProvider provider = new("""
            ```csharp
            public sealed class GameState
            {
                public Mark CurrentPlayer => Mark.X;
            }
            ```
            """);
        RecordingMutationService mutations = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            new CapturingAgentToolFactory { ReturnWorkspaceFileView = true },
            NullLoggerFactory.Instance,
            new StaticInspectionService(baseline),
            mutations);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"),
            AgentRole.Implementer,
            new("replace the exact game state file"),
            [new("src/GameState.cs")]));

        Assert.Null(result.Error);
        Assert.Contains("namespace TicTacToe.Core;", mutations.Content, StringComparison.Ordinal);
        Assert.Contains("public enum Mark", mutations.Content, StringComparison.Ordinal);
        Assert.Contains("public sealed class GameState", mutations.Content, StringComparison.Ordinal);
        Assert.Equal([DotNetOperation.Build, DotNetOperation.Test], mutations.Operations);
    }

    [Fact]
    public async Task Structured_local_edit_rejects_a_dependency_type_for_the_target_file()
    {
        const string baseline = """
            namespace TicTacToe.Tests;

            public sealed class ImplementationTests { }
            """;
        CapturingModelProvider provider = new("""
            ```csharp
            namespace TicTacToe.Core;

            public sealed class MinimaxSolver { }
            ```
            """);
        RecordingMutationService mutations = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            new CapturingAgentToolFactory { ReturnWorkspaceFileView = true },
            NullLoggerFactory.Instance,
            new StaticInspectionService(baseline),
            mutations);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"),
            AgentRole.Implementer,
            new("replace the exact test file"),
            [new("tests/UnitTest1.cs")]));

        Assert.Equal("structured_source_identity_mismatch", result.ErrorCode?.Value);
        Assert.Contains("wrong C# namespace", result.Error?.Value, StringComparison.Ordinal);
        Assert.Null(mutations.Content);
        Assert.Empty(mutations.Operations);
    }

    [Fact]
    public async Task Structured_local_edit_never_compiles_explanatory_prose_as_source()
    {
        CapturingModelProvider provider = new(
            "I would preserve the existing namespace and update only the failing method.");
        RecordingMutationService mutations = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            new CapturingAgentToolFactory { ReturnWorkspaceFileView = true },
            NullLoggerFactory.Instance,
            new StaticInspectionService("namespace Example; public sealed class GameState { }"),
            mutations);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"),
            AgentRole.Implementer,
            new("repair the exact game state file"),
            [new("src/GameState.cs")]));

        Assert.Equal("invalid_structured_file_edit", result.ErrorCode?.Value);
        Assert.Null(mutations.Content);
        Assert.Empty(mutations.Operations);
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
            ModelAccess.Local, AgentReasoningPolicy.ProviderDefault,
            provider),
        ErrorCode: null,
        Error: null);


}
