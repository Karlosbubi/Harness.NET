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
        internal string? WorkspaceFileErrorCode { get; init; }

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
                            WorkspaceFileErrorCode is null ? "bounded file" : string.Empty,
                            WorkspaceFileErrorCode is null ? new string('a', 64) : null,
                            WorkspaceFileErrorCode is null ? 12 : 0,
                            IsTruncated: false,
                            WorkspaceFileErrorCode,
                            WorkspaceFileErrorCode is null
                                ? null
                                : "The requested file does not exist.")
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

    private sealed class ToolCallingModelProvider(bool emptyFinalResponse = false) : IModelProvider
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
                emptyFinalResponse ? string.Empty : "finished after edit",
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
    }}
