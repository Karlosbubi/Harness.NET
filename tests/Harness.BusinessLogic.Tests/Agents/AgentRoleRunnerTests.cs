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
    public async Task Gives_implementer_one_in_session_correction_when_it_narrates_without_tools()
    {
        NarratingThenToolCallingModelProvider provider = new();
        CapturingAgentToolFactory tools = new();
        AgentRoleRunner runner = new(
            new StubRouteResolver(role => Route(role, "tool-model", provider)),
            tools,
            NullLoggerFactory.Instance);

        AgentRunResult result = await runner.RunAsync(new(
            new("goal-tools"),
            AgentRole.Implementer,
            new("implement"),
            [new("src")]));

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
            IReadOnlyList<AgentFileArea> fileAreas,
            ModelAccess modelAccess) => [];
    }

    private sealed class CapturingAgentToolFactory : IAgentToolFactory
    {
        internal string? RelativePath { get; private set; }
        internal string? EditedContent { get; private set; }
        internal bool ReturnWorkspaceFileView { get; init; }

        public IList<AITool> Create(
            AgentRole role,
            GoalId goalId,
            IReadOnlyList<AgentFileArea> fileAreas,
            ModelAccess modelAccess) =>
        [
            AIFunctionFactory.Create(
                (string relativePath) =>
                {
                    RelativePath = relativePath;
                    return ReturnWorkspaceFileView
                        ? (object)new WorkspaceFileView(
                            relativePath,
                            "bounded file",
                            new string('a', 64),
                            12,
                            IsTruncated: false,
                            ErrorCode: null,
                            Error: null)
                        : "bounded file";
                },
                new()
                {
                    Name = "read_file",
                    Description = "Read a bounded file.",
                }),
            AIFunctionFactory.Create(
                (string relativePath, string content) =>
                {
                    RelativePath = relativePath;
                    EditedContent = content;
                    return content == "bootstrapped source"
                        ? (object)new FileEditView(
                            "goal-tools",
                            new("structured-edit"),
                            relativePath,
                            PreviousSha256: "old",
                            NewSha256: "new",
                            BytesWritten: content.Length,
                            WasCreated: false,
                            ErrorCode: null,
                            Error: null)
                        : "edit applied";
                },
                new()
                {
                    Name = "apply_file_edit",
                    Description = "Apply a bounded file edit.",
                }),
            AIFunctionFactory.Create(
                (string relativePath, int line, int character) => "symbol",
                new()
                {
                    Name = "get_symbol_info",
                    Description = "Inspect a symbol.",
                }),
            AIFunctionFactory.Create(
                (string relativePath, int line, int character) => "definition",
                new()
                {
                    Name = "find_symbol_definition",
                    Description = "Find a definition.",
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

            if (Requests.Count == 2)
            {
                yield return new(
                    string.Empty,
                    string.Empty,
                    Done: true,
                    DoneReason: "tool_calls",
                    new(8, 3),
                    Error: null,
                    [new(new("call-2"), new("apply_file_edit"),
                        new("{\"relativePath\":\"src/Program.cs\",\"content\":\"updated source\"}"))]);
                yield break;
            }

            yield return new(
                "finished after edit",
                string.Empty,
                Done: true,
                DoneReason: "stop",
                new(12, 4),
                Error: null);
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NarratingThenToolCallingModelProvider : IModelProvider
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
                yield return new("I will inspect the file.", string.Empty, true, "stop",
                    new(4, 2), Error: null);
                yield break;
            }

            if (Requests.Count == 2)
            {
                yield return new(
                    string.Empty,
                    string.Empty,
                    Done: true,
                    DoneReason: "tool_calls",
                    new(8, 3),
                    Error: null,
                    [new(new("call-correction"), new("read_file"),
                        new("{\"relativePath\":\"src/Program.cs\"}"))]);
                yield break;
            }

            yield return new("finished after correction tool", string.Empty, true, "stop",
                new(12, 4), Error: null);
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BootstrapMutationModelProvider : IModelProvider
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
            yield return new("```csharp\nbootstrapped source\n```", string.Empty, true,
                "stop", new(10, 3), Error: null);
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RepairingModelProvider : IModelProvider
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
            string output = Requests.Count == 1
                ? "```csharp\nConsole.WriteLine(\"bad\");\n```"
                : """
                  <<<<<<< SEARCH
                  Console.WriteLine("bad");
                  =======
                  Console.WriteLine("good");
                  >>>>>>> REPLACE
                  """;
            yield return new(output, string.Empty, true, "stop", new(10, 3), Error: null);
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

    private sealed class StaticInspectionService(string content = "baseline")
        : IGoalWorkspaceInspectionService
    {
        internal List<string> Paths { get; } = [];

        public ValueTask<WorkspaceFileView> ReadFileAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(relativePath);
            return ValueTask.FromResult(new WorkspaceFileView(
                relativePath,
                content,
                new string('a', 64),
                8,
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<WorkspaceTextSearchView> SearchTextAsync(
            GoalId goalId, GoalWorkspaceScope scope, string query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceGitStateView> InspectGitAsync(
            GoalId goalId, GoalWorkspaceScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceDotNetInfoView> InspectDotNetAsync(
            GoalId goalId, GoalWorkspaceScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingMutationService(
        int testFailures = 0,
        string failureOutput = "A focused behavioral contract failed.") : IWorkspaceMutationService
    {
        internal string? Content { get; private set; }
        internal List<DotNetOperation> Operations { get; } = [];

        public ValueTask<FileEditView> ApplyFileEditAsync(
            FileEditRequest request,
            CancellationToken cancellationToken = default)
        {
            Content = request.Content;
            return ValueTask.FromResult(new FileEditView(
                request.GoalId,
                request.CorrelationId,
                request.Path,
                request.ExpectedSha256,
                new string('b', 64),
                request.Content.Length,
                WasCreated: false,
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<DotNetOperationView> RunDotNetAsync(
            DotNetOperationRequest request,
            CancellationToken cancellationToken = default)
        {
            Operations.Add(request.Operation);
            bool failed = request.Operation is DotNetOperation.Test && testFailures-- > 0;
            return ValueTask.FromResult(new DotNetOperationView(
                request.GoalId,
                request.CorrelationId,
                request.Operation,
                "Harness.slnx",
                ExitCode: failed ? 1 : 0,
                StandardOutput: failed ? failureOutput : string.Empty,
                StandardError: string.Empty,
                IsOutputTruncated: false,
                IsErrorTruncated: false,
                WasCancelled: false,
                DurationMilliseconds: 1,
                ErrorCode: null,
                Error: null));
        }
    }
}
