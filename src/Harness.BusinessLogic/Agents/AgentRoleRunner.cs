using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Agents;

internal sealed partial class AgentRoleRunner : IAgentRoleRunner
{
    private const int MaximumTaskCharacters = 64 * 1024;
    private const int MaximumModelIterationsPerRequest = 6;
    private const int MaximumStructuredEditAttempts = 4;
    private const int MaximumRepairSourceCharacters = 64 * 1024;
    private readonly IGoalModelRouteResolver routeResolver;
    private readonly IAgentToolFactory toolFactory;
    private readonly ILoggerFactory loggerFactory;
    private readonly IGoalWorkspaceInspectionService? inspectionService;
    private readonly IWorkspaceMutationService? mutationService;
    private readonly AgentActivityService? activityService;

    public AgentRoleRunner(
        IGoalModelRouteResolver routeResolver,
        IAgentToolFactory toolFactory,
        ILoggerFactory loggerFactory,
        IGoalWorkspaceInspectionService? inspectionService = null,
        IWorkspaceMutationService? mutationService = null,
        AgentActivityService? activityService = null)
    {
        ArgumentNullException.ThrowIfNull(routeResolver);
        ArgumentNullException.ThrowIfNull(toolFactory);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        this.routeResolver = routeResolver;
        this.toolFactory = toolFactory;
        this.loggerFactory = loggerFactory;
        this.inspectionService = inspectionService;
        this.mutationService = mutationService;
        this.activityService = activityService;
    }

    public async ValueTask<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.GoalId is null ||
            string.IsNullOrWhiteSpace(request.GoalId.Value) ||
            request.Task is null ||
            string.IsNullOrWhiteSpace(request.Task.Value) ||
            request.Task.Value.Length > MaximumTaskCharacters ||
            !Enum.IsDefined(request.Role) ||
            !ValidFileAreas(request.Role, request.FileAreas))
        {
            return new(
                request?.Role ?? AgentRole.Lead,
                Output: null,
                new("invalid_agent_request"),
                new("An agent role and a task of at most 65536 characters are required."));
        }

        try
        {
            GoalModelRouteResult resolved = await routeResolver.ResolveAsync(
                request.GoalId, request.Role, cancellationToken);
            if (resolved.Route is null)
            {
                return new(request.Role, Output: null, resolved.ErrorCode, resolved.Error);
            }

            GoalModelRoute route = resolved.Route;

            IList<AITool> tools = toolFactory.Create(
                request.Role, request.GoalId, request.FileAreas ?? [], route.Access);
            BootstrapInspection? bootstrap = await BootstrapExactFileInspectionAsync(
                request, tools, cancellationToken);
            bool inspectionBootstrapped = bootstrap is not null;
            bool structuredLocalFileEdit =
                inspectionBootstrapped && route.Access is ModelAccess.Local;
            ModelProviderChatClient providerClient = new(
                route.Provider,
                route.Model,
                route.Access is ModelAccess.Remote ? route.GoalId : null,
                route.Role,
                inspectionBootstrapped, structuredLocalFileEdit,
                route.ReasoningPolicy,
                request.GoalId,
                activityService);
            IChatClient chatClient = new ChatClientBuilder(providerClient)
                .UseFunctionInvocation(loggerFactory, functionClient =>
                {
                    functionClient.MaximumIterationsPerRequest =
                        MaximumModelIterationsPerRequest;
                    functionClient.MaximumConsecutiveErrorsPerRequest = 3;
                })
                .Build();
            AIAgent agent = new ChatClientAgent(
                chatClient,
                new ChatClientAgentOptions
                {
                    Name = AgentRolePromptPolicy.Name(request.Role),
                    Description = AgentRolePromptPolicy.Description(request.Role),
                    ChatOptions = new ChatOptions
                    {
                        Instructions = AgentRolePromptPolicy.Instructions(request.Role) +
                            (inspectionBootstrapped
                                ? " Harness has already completed the mandatory typed read of the " +
                                  "exact target and supplied its result in the task. Do not repeat " +
                                  "that read; use its sha256 and make the required mutation now."
                                : string.Empty) +
                            (structuredLocalFileEdit
                                ? " Return only the complete replacement source in one fenced code " +
                                  "block. Do not wrap source code in JSON because JSON escaping can " +
                                  "change C# string literals. Include no narration outside the fence."
                                : string.Empty),
                        Tools = tools,
                    },
                    UseProvidedChatClientAsIs = true,
                },
                loggerFactory,
                services: null);
            AgentSession session = await agent.CreateSessionAsync(cancellationToken);
            string task = bootstrap is null
                ? request.Task.Value.Trim()
                : bootstrap.Context + "\n\n" + request.Task.Value.Trim();
            AgentResponse response = await agent.RunAsync(
                task,
                session,
                cancellationToken: cancellationToken);
            if (structuredLocalFileEdit &&
                providerClient.ToolCallCount == 0 &&
                bootstrap is not null)
            {
                AgentRunResult? editResult = null;
                BootstrapInspection activeBootstrap = bootstrap;
                string? proposalBaseContent = null;
                for (int attempt = 1; attempt <= MaximumStructuredEditAttempts; attempt++)
                {
                    editResult = await ApplyStructuredLocalFileEditAsync(
                        request.Role, request.GoalId, response.Text, activeBootstrap, tools,
                        proposalBaseContent, cancellationToken);
                    string? candidateContent = StructuredProposalContent(
                        response.Text,
                        proposalBaseContent ?? activeBootstrap.Content);
                    if (editResult.Output is not null ||
                        attempt == MaximumStructuredEditAttempts)
                    {
                        return editResult;
                    }

                    string rejection = editResult.Error?.Value ??
                        "The deterministic edit validator rejected the proposal.";
                    const int maximumRejectionCharacters = 16_000;
                    if (rejection.Length > maximumRejectionCharacters)
                    {
                        rejection = rejection[..maximumRejectionCharacters];
                    }
                    string rejectedProposal = candidateContent ?? proposalBaseContent ??
                        activeBootstrap.Content ?? response.Text.Trim();
                    if (rejectedProposal.Length > MaximumRepairSourceCharacters)
                    {
                        rejectedProposal = rejectedProposal[..MaximumRepairSourceCharacters];
                    }

                    BootstrapInspection? refreshed = await BootstrapExactFileInspectionAsync(
                        request, tools, cancellationToken);
                    if (refreshed is not null)
                    {
                        activeBootstrap = refreshed;
                    }
                    string diagnosticDependencyContext =
                        await ReadDiagnosticDependenciesAsync(
                            request.GoalId,
                            activeBootstrap.RelativePath,
                            rejection,
                            cancellationToken);

                    session = await agent.CreateSessionAsync(cancellationToken);
                    response = await agent.RunAsync(
                        "CORRECT THE REJECTED FILE PROPOSAL WITH THE SMALLEST COHERENT EDIT. " +
                        "Preserve every working region, the exact namespace, and the required public " +
                        "API. Use cited Roslyn diagnostic coordinates and test stack frames as the " +
                        "repair targets; do not redesign unrelated code. Consumer code and tests " +
                        "must use only public members shown in dependency context. Never call a " +
                        "private constructor or method. Return one to four exact replacement blocks " +
                        "against REPAIR BASE SOURCE using this format and no narration or fence:\n" +
                        "<<<<<<< SEARCH\nexact current text\n=======\nreplacement text\n" +
                        ">>>>>>> REPLACE\n" +
                        "Each SEARCH must occur exactly once. Include the smallest enclosing method " +
                        "or expression needed for an unambiguous repair. Return a complete fenced " +
                        "source file only when no safe local replacement exists. If the same test " +
                        "still fails, do not make a cosmetic or algebraically equivalent change: " +
                        "derive the required invariant from its expected/actual values and cited " +
                        "source, then mentally check boundary states before responding. When an " +
                        "assertion expects a value but the cited target frame throws, preserve the " +
                        "goal's exact public signature and return its specified sentinel/default " +
                        "value; do not introduce nullable API or retain a no-result exception unless " +
                        "the authoritative contract requires it.\n\n" +
                        "DETERMINISTIC REJECTION EVIDENCE\n" + rejection + "\n\n" +
                        "REPAIR BASE SOURCE\n" + rejectedProposal + "\n\n" +
                        activeBootstrap.DependencyContext + diagnosticDependencyContext +
                        "\n\nBOUNDED TASK\n" +
                        ConciseDelegatedTask(request.Task.Value),
                        session,
                        cancellationToken: cancellationToken);
                    proposalBaseContent = candidateContent ?? proposalBaseContent;
                }

                return editResult!;
            }
            if (request.Role is AgentRole.Implementer &&
                tools.Count > 0 &&
                providerClient.ToolCallCount == 0)
            {
                AIAgent correctionAgent = new ChatClientAgent(
                    chatClient,
                    new ChatClientAgentOptions
                    {
                        Name = "implementer-tool-correction",
                        Description = "Executes a bounded task after a text-only response.",
                        ChatOptions = new ChatOptions
                        {
                            Instructions = inspectionBootstrapped
                                ? "You must act through the supplied typed tools. Harness already " +
                                  "performed the exact target read supplied in the task. Your first " +
                                  "response must apply the complete mutation using that sha256; never " +
                                  "answer with narration or a plan. Then validate the mutation. " +
                                  "Correct tool errors with a new correlation identifier."
                                : "You must act through the supplied typed tools. Your first response " +
                                  "must call read_file for the exact authorized target; never answer " +
                                  "with narration or a plan. Then apply and validate a complete mutation. " +
                                  "Correct tool errors with a new correlation identifier.",
                            Tools = tools,
                        },
                        UseProvidedChatClientAsIs = true,
                    },
                    loggerFactory,
                    services: null);
                session = await correctionAgent.CreateSessionAsync(cancellationToken);
                response = await correctionAgent.RunAsync(
                    "TOOL EXECUTION REQUIRED.\n\nBOUNDED TASK AND FILE GRANTS\n" +
                    (bootstrap is null ? string.Empty : bootstrap.Context + "\n\n") +
                    ConciseDelegatedTask(request.Task.Value),
                    session,
                    cancellationToken: cancellationToken);
            }
            if (request.Role is AgentRole.Reviewer &&
                tools.Count > 0 &&
                (!providerClient.CalledTool("inspect_git") ||
                 !providerClient.CalledTool("list_tool_evidence")))
            {
                AIAgent correctionAgent = new ChatClientAgent(
                    chatClient,
                    new ChatClientAgentOptions
                    {
                        Name = "reviewer-tool-correction",
                        Description = "Performs an evidence-based review after a text-only response.",
                        ChatOptions = new ChatOptions
                        {
                            Instructions =
                                "You must inspect the supplied work through typed tools before deciding. " +
                                "Call inspect_git and list_tool_evidence first, inspect relevant files or " +
                                "diagnostics as needed, then return the required accept/revise JSON. Do not " +
                                "base a revision solely on evidence you declined to inspect.",
                            Tools = tools,
                        },
                        UseProvidedChatClientAsIs = true,
                    },
                    loggerFactory,
                    services: null);
                session = await correctionAgent.CreateSessionAsync(cancellationToken);
                response = await correctionAgent.RunAsync(
                    "INDEPENDENT TOOL INSPECTION REQUIRED.\n\n" + request.Task.Value,
                    session,
                    cancellationToken: cancellationToken);
            }

            if (request.Role is AgentRole.Reviewer &&
                tools.Count > 0 &&
                (!providerClient.CalledTool("inspect_git") ||
                 !providerClient.CalledTool("list_tool_evidence")))
            {
                return new(
                    request.Role,
                    Output: null,
                    new("reviewer_evidence_missing"),
                    new("The Reviewer did not inspect both the worktree diff and durable " +
                        "tool evidence after one bounded correction."));
            }

            return AgentRunResultPolicy.Final(
                request.Role, response.Text, providerClient.ToolCallCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            bool costLimitReached = exception.Message.Contains(
                "remote_cost_cap_exceeded",
                StringComparison.Ordinal);
            return new(
                request.Role,
                Output: null,
                new(costLimitReached ? "remote_cost_cap_exceeded" : "agent_run_failed"),
                new(exception.Message));
        }
    }

    private async ValueTask<BootstrapInspection?> BootstrapExactFileInspectionAsync(
        AgentRunRequest request,
        IList<AITool> tools,
        CancellationToken cancellationToken)
    {
        if (request.Role is not AgentRole.Implementer ||
            request.FileAreas is not { Count: 1 } ||
            !Path.HasExtension(request.FileAreas[0].Value) ||
            tools.OfType<AIFunction>().SingleOrDefault(tool => tool.Name == "read_file") is not
            { } readFile)
        {
            return null;
        }

        object? result = inspectionService is null
            ? await readFile.InvokeAsync(
                new AIFunctionArguments
                {
                    ["relativePath"] = request.FileAreas[0].Value,
                },
                cancellationToken)
            : await inspectionService.ReadFileAsync(
                request.GoalId,
                GoalWorkspaceScope.ApprovedWorktree,
                request.FileAreas[0].Value,
                cancellationToken);
        if (result is null)
        {
            return null;
        }

        string serialized = result is string text
            ? text
            : JsonSerializer.Serialize(result, result.GetType());
        WorkspaceFileView? file = result as WorkspaceFileView;
        if (file is null)
        {
            try
            {
                file = JsonSerializer.Deserialize<WorkspaceFileView>(serialized,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                // Function adapters may wrap a typed result. The serialized result below remains
                // authoritative and is searched for the complete-read digest.
            }
        }

        if (file?.ErrorCode is not null)
        {
            return null;
        }

        string? sha256 = file is { IsTruncated: false, ErrorCode: null }
            ? file.Sha256
            : null;
        if (string.IsNullOrWhiteSpace(sha256))
        {
            Match digest = Regex.Match(serialized,
                "(?<![a-fA-F0-9])(?<digest>[a-fA-F0-9]{64})(?![a-fA-F0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            sha256 = digest.Success ? digest.Groups["digest"].Value : null;
        }

        if (string.IsNullOrWhiteSpace(sha256))
        {
            const int maximumDiagnosticCharacters = 2_000;
            string diagnostic = serialized.Length <= maximumDiagnosticCharacters
                ? serialized
                : serialized[..maximumDiagnosticCharacters];
            throw new InvalidOperationException(
                "Exact-file typed inspection did not return a complete SHA256-bearing result: " +
                diagnostic);
        }

        string context = "DETERMINISTIC TYPED INSPECTION (already completed by Harness)\n" +
            "Target: " + request.FileAreas[0].Value + "\n" +
            "read_file result: " + serialized;
        string dependencyContext = string.Empty;
        if (inspectionService is not null)
        {
            string[] dependencyPaths = Regex.Matches(request.Task.Value,
                    @"(?<![A-Za-z0-9_./-])(?<path>(?:src|tests)/[A-Za-z0-9_./-]+\.cs)(?![A-Za-z0-9_./-])",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Groups["path"].Value)
                .Where(path => !string.Equals(
                    path, request.FileAreas[0].Value, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToArray();
            foreach (string dependencyPath in dependencyPaths)
            {
                WorkspaceFileView dependency = await inspectionService.ReadFileAsync(
                    request.GoalId,
                    GoalWorkspaceScope.ApprovedWorktree,
                    dependencyPath,
                    cancellationToken);
                if (dependency is { ErrorCode: null, IsTruncated: false })
                {
                    dependencyContext += "\nDependency: " + dependencyPath +
                        "\nread_file result: " +
                        JsonSerializer.Serialize(dependency);
                }
            }
        }
        return new(context + dependencyContext, dependencyContext,
            request.FileAreas[0].Value, sha256, file?.Content);
    }

    private async ValueTask<string> ReadDiagnosticDependenciesAsync(
        GoalId goalId,
        string targetPath,
        string rejection,
        CancellationToken cancellationToken)
    {
        if (inspectionService is null)
        {
            return string.Empty;
        }

        string[] citedPaths = Regex.Matches(
                rejection,
                @"(?<path>(?:src|tests)/[A-Za-z0-9_./-]+\.cs)(?![A-Za-z0-9_./-])",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["path"].Value)
            .Where(path => !string.Equals(path, targetPath, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
        if (citedPaths.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder context = new();
        foreach (string path in citedPaths)
        {
            WorkspaceFileView dependency = await inspectionService.ReadFileAsync(
                goalId,
                GoalWorkspaceScope.ApprovedWorktree,
                path,
                cancellationToken);
            if (dependency is { ErrorCode: null, IsTruncated: false })
            {
                context.Append("\n\nDETERMINISTIC CITED SOURCE (read-only): ")
                    .Append(path)
                    .Append("\nread_file result: ")
                    .Append(JsonSerializer.Serialize(dependency));
            }
        }

        const int maximumCharacters = 32_000;
        return context.Length <= maximumCharacters
            ? context.ToString()
            : context.ToString(0, maximumCharacters);
    }

    private static string ConciseDelegatedTask(string task)
    {
        const string objectiveMarker = "FULL GOAL OBJECTIVE (AUTHORITATIVE)";
        const string planMarker = "APPROVED PLAN";
        const string delegatedMarker = "DELEGATED TASK";
        int objectiveIndex = task.IndexOf(objectiveMarker, StringComparison.Ordinal);
        int planIndex = task.IndexOf(planMarker, StringComparison.Ordinal);
        int delegatedIndex = task.LastIndexOf(delegatedMarker, StringComparison.Ordinal);
        string concise;
        if (objectiveIndex >= 0 && planIndex > objectiveIndex && delegatedIndex >= 0)
        {
            // Keep the authoritative contract during repair. The approved plan is useful to the
            // first implementation call, but repeating it on every deterministic correction both
            // wastes context and previously caused this helper to discard the actual goal rules.
            string objective = task[objectiveIndex..planIndex].Trim();
            string delegated = task[delegatedIndex..].Trim();
            concise = objective + "\n\n" + delegated;
        }
        else
        {
            concise = delegatedIndex >= 0 ? task[delegatedIndex..] : task;
        }

        const int maximumCharacters = 16_000;
        return concise.Length <= maximumCharacters
            ? concise.Trim()
            : concise[..maximumCharacters].Trim();
    }

    private static bool ValidFileAreas(
        AgentRole role,
        IReadOnlyList<AgentFileArea>? fileAreas)
    {
        if (role is not AgentRole.Implementer)
        {
            return fileAreas is null or { Count: 0 };
        }

        return fileAreas is { Count: > 0 and <= 32 } && fileAreas.All(area =>
            area is not null && AgentToolFactory.ValidFileArea(area.Value));
    }

}
