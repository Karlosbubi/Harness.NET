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

internal sealed class AgentRoleRunner : IAgentRoleRunner
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

    public AgentRoleRunner(
        IGoalModelRouteResolver routeResolver,
        IAgentToolFactory toolFactory,
        ILoggerFactory loggerFactory,
        IGoalWorkspaceInspectionService? inspectionService = null,
        IWorkspaceMutationService? mutationService = null)
    {
        ArgumentNullException.ThrowIfNull(routeResolver);
        ArgumentNullException.ThrowIfNull(toolFactory);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        this.routeResolver = routeResolver;
        this.toolFactory = toolFactory;
        this.loggerFactory = loggerFactory;
        this.inspectionService = inspectionService;
        this.mutationService = mutationService;
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
                inspectionBootstrapped,
                structuredLocalFileEdit);
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
                    Name = Name(request.Role),
                    Description = Description(request.Role),
                    ChatOptions = new ChatOptions
                    {
                        Instructions = Instructions(request.Role) +
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
            return new(request.Role, new(response.Text), ErrorCode: null, Error: null);
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

    private async ValueTask<AgentRunResult> ApplyStructuredLocalFileEditAsync(
        AgentRole role,
        GoalId goalId,
        string output,
        BootstrapInspection bootstrap,
        IList<AITool> tools,
        string? proposalBaseContent,
        CancellationToken cancellationToken)
    {
        string? content = StructuredProposalContent(
            output,
            proposalBaseContent ?? bootstrap.Content);

        content = PreserveSourceEnvelope(content, bootstrap);
        string? identityError = ValidateSourceIdentity(content, bootstrap);
        if (identityError is not null)
        {
            return new(role, Output: null, new("structured_source_identity_mismatch"),
                new(identityError));
        }
        if (string.IsNullOrWhiteSpace(content) ||
            tools.OfType<AIFunction>().SingleOrDefault(tool => tool.Name == "apply_file_edit") is not
            { } applyFileEdit)
        {
            return new(role, Output: null, new("invalid_structured_file_edit"),
                new("The local model returned an empty file proposal or no typed edit tool is available."));
        }

        string correlationId = "structured-local-edit-" + Guid.NewGuid().ToString("N");
        object? result = mutationService is null
            ? await applyFileEdit.InvokeAsync(
                new AIFunctionArguments
                {
                    ["correlationId"] = correlationId,
                    ["relativePath"] = bootstrap.RelativePath,
                    ["expectedSha256"] = bootstrap.Sha256,
                    ["content"] = content,
                },
                cancellationToken)
            : await mutationService.ApplyFileEditAsync(new(
                goalId.Value,
                new ToolCorrelationId(correlationId),
                bootstrap.RelativePath,
                bootstrap.Sha256,
                content,
                FileEditOrigin.Model), cancellationToken);
        FileEditView? edit = result as FileEditView;
        if (edit is null && result is not null)
        {
            edit = JsonSerializer.Deserialize<FileEditView>(
                JsonSerializer.Serialize(result, result.GetType()),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        if (edit is not { ErrorCode: null })
        {
            string detail = edit is null
                ? result is null
                    ? "No edit result was returned."
                    : JsonSerializer.Serialize(result, result.GetType())
                : DescribeRejectedEdit(edit);
            return new(role, Output: null, new("structured_file_edit_rejected"), new(detail));
        }


        if (mutationService is not null)
        {
            DotNetOperationView build = await mutationService.RunDotNetAsync(new(
                goalId.Value,
                new ToolCorrelationId("structured-build-" + Guid.NewGuid().ToString("N")),
                DotNetOperation.Build), cancellationToken);
            if (!Succeeded(build))
            {
                return ValidationFailure(role, build);
            }

            // A warning-free build proves only that the candidate compiles. Run the repository's
            // deterministic tests after every C# edit so a production task cannot be marked
            // complete while an existing behavioral contract is already failing. The correction
            // loop still owns the exact active file, so failures are repaired at their source.
            if (bootstrap.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                DotNetOperationView test = await mutationService.RunDotNetAsync(new(
                    goalId.Value,
                    new ToolCorrelationId("structured-test-" + Guid.NewGuid().ToString("N")),
                    DotNetOperation.Test), cancellationToken);
                if (!Succeeded(test))
                {
                    return ValidationFailure(role, test);
                }
            }
        }

        return new(role, new(JsonSerializer.Serialize(new
        {
            status = "complete",
            summary = $"Applied the typed edit to {edit.Path}.",
            validation = edit.AppliedCodeValidation is null
                ? Array.Empty<string>()
                : ["Deterministic code validation completed."],
            remaining = Array.Empty<string>(),
        })), ErrorCode: null, Error: null);
    }

    private static string? StructuredProposalContent(string output, string? repairBase)
    {
        string proposal = output.Trim();
        if (proposal.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLine = proposal.IndexOf('\n');
            int closingFence = proposal.LastIndexOf("```", StringComparison.Ordinal);
            return firstLine >= 0 && closingFence > firstLine
                ? proposal[(firstLine + 1)..closingFence].Trim()
                : null;
        }
        else if (proposal.StartsWith('{'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(proposal);
                return document.RootElement.GetProperty("content").GetString();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (!proposal.StartsWith("<<<<<<< SEARCH", StringComparison.Ordinal))
        {
            // The structured local path deliberately suppresses tools and accepts only an
            // unambiguous machine-readable proposal. Treat narration, Markdown headings, and
            // other plain text as a format failure instead of feeding it to Roslyn as C#.
            return null;
        }

        if (string.IsNullOrEmpty(repairBase))
        {
            return null;
        }

        const string replacementPattern =
            @"<<<<<<< SEARCH\r?\n(?<search>.*?)\r?\n=======\r?\n(?<replacement>.*?)\r?\n>>>>>>> REPLACE";
        MatchCollection replacements = Regex.Matches(
            proposal,
            replacementPattern,
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (replacements.Count is < 1 or > 4 ||
            !string.IsNullOrWhiteSpace(Regex.Replace(
                proposal,
                replacementPattern,
                string.Empty,
                RegexOptions.Singleline | RegexOptions.CultureInvariant)))
        {
            return null;
        }

        string content = repairBase;
        foreach (Match replacement in replacements)
        {
            string search = MatchLineEndings(
                replacement.Groups["search"].Value,
                content);
            string replaceWith = MatchLineEndings(
                replacement.Groups["replacement"].Value,
                content);
            if (string.IsNullOrEmpty(search))
            {
                return null;
            }

            int first = content.IndexOf(search, StringComparison.Ordinal);
            if (first < 0 ||
                content.IndexOf(search, first + search.Length, StringComparison.Ordinal) >= 0)
            {
                return null;
            }

            content = content[..first] + replaceWith + content[(first + search.Length)..];
        }

        return content;
    }

    private static string MatchLineEndings(string value, string source)
    {
        string normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        return source.Contains("\r\n", StringComparison.Ordinal)
            ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : normalized;
    }

    private static bool Succeeded(DotNetOperationView operation) =>
        operation.ErrorCode is null &&
        operation.ExitCode == 0 &&
        !operation.WasCancelled;

    private static AgentRunResult ValidationFailure(
        AgentRole role,
        DotNetOperationView operation)
    {
        string detail = $"Deterministic {operation.Operation} failed with exit code " +
            $"{operation.ExitCode?.ToString() ?? "unknown"}.\n" +
            operation.StandardOutput + "\n" + operation.StandardError;
        const int maximumDetailCharacters = 16_000;
        if (detail.Length > maximumDetailCharacters)
        {
            detail = detail[^maximumDetailCharacters..];
        }

        return new(role, Output: null, new("structured_validation_failed"), new(detail));
    }

    private static string? PreserveSourceEnvelope(
        string? proposal,
        BootstrapInspection bootstrap)
    {
        if (string.IsNullOrWhiteSpace(proposal) ||
            !bootstrap.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            proposal.Contains("namespace ", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(bootstrap.Content))
        {
            return proposal;
        }

        string typeName = Path.GetFileNameWithoutExtension(bootstrap.RelativePath);
        string declarationPattern =
            "(?m)^[ \\t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly|ref|file)[ \\t]+)*(?:class|record(?:[ \\t]+class)?|struct|interface|enum)[ \\t]+" +
            Regex.Escape(typeName) + "\\b";
        Match baselineDeclaration = Regex.Match(
            bootstrap.Content,
            declarationPattern,
            RegexOptions.CultureInvariant);
        Match proposalDeclaration = Regex.Match(
            proposal,
            declarationPattern,
            RegexOptions.CultureInvariant);
        if (baselineDeclaration.Success && proposalDeclaration.Success)
        {
            return bootstrap.Content[..baselineDeclaration.Index] +
                proposal[proposalDeclaration.Index..].Trim();
        }

        Match namespaceDeclaration = Regex.Match(
            bootstrap.Content,
            @"(?m)^[ \t]*namespace[ \t]+[^;{]+;[ \t]*(?:\r?\n)?",
            RegexOptions.CultureInvariant);
        return namespaceDeclaration.Success
            ? bootstrap.Content[..(namespaceDeclaration.Index + namespaceDeclaration.Length)] +
              proposal.Trim()
            : proposal;
    }

    private static string? ValidateSourceIdentity(
        string? proposal,
        BootstrapInspection bootstrap)
    {
        if (string.IsNullOrWhiteSpace(proposal) ||
            !bootstrap.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(bootstrap.Content))
        {
            return null;
        }

        Match baselineNamespace = Regex.Match(
            bootstrap.Content,
            @"(?m)^[ \t]*namespace[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_.]*)[ \t]*[;{]",
            RegexOptions.CultureInvariant);
        if (baselineNamespace.Success && !Regex.IsMatch(
                proposal,
                @"(?m)^[ \t]*namespace[ \t]+" +
                Regex.Escape(baselineNamespace.Groups["name"].Value) + @"[ \t]*[;{]",
                RegexOptions.CultureInvariant))
        {
            return "The proposal is for the wrong C# namespace. Preserve namespace " +
                baselineNamespace.Groups["name"].Value + " from the exact target file.";
        }

        const string typePattern =
            @"(?m)^[ \t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly|ref|file)[ \t]+)*(?:class|record(?:[ \t]+class)?|struct|interface|enum)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b";
        string[] baselineTypes = Regex.Matches(
                bootstrap.Content, typePattern, RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (baselineTypes.Length > 0 && !baselineTypes.Any(typeName => Regex.IsMatch(
                proposal,
                @"(?m)^[ \t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly|ref|file)[ \t]+)*(?:class|record(?:[ \t]+class)?|struct|interface|enum)[ \t]+" +
                Regex.Escape(typeName) + @"\b",
                RegexOptions.CultureInvariant)))
        {
            return "The proposal is for the wrong C# type. The complete replacement for " +
                bootstrap.RelativePath + " must preserve at least one target declaration: " +
                string.Join(", ", baselineTypes) + ". Do not return a dependency type.";
        }

        return null;
    }

    private static string DescribeRejectedEdit(FileEditView edit)
    {
        List<string> lines =
        [
            $"Typed edit rejected: {edit.ErrorCode}: {edit.Error}",
        ];
        if (edit.CandidateCodeValidation is { } validation)
        {
            WorkbenchCodeValidationDiagnostic[] introduced = validation.Diagnostics
                .Where(item => item.Kind is WorkbenchCodeDiagnosticDeltaKind.Introduced)
                .Where(item => item.Diagnostic.Severity is
                    WorkbenchCodeDiagnosticSeverity.Warning or
                    WorkbenchCodeDiagnosticSeverity.Error)
                .Take(20)
                .ToArray();
            lines.AddRange(introduced
                .Select(item =>
                    $"{item.Diagnostic.Id.Value}: {item.Diagnostic.Message.Value} " +
                    $"at {item.Diagnostic.Path.Value}:" +
                    $"{item.Diagnostic.Range.Start.Line + 1}"));
            lines.AddRange(introduced
                .Select(item => DiagnosticRepairGuidance(item.Diagnostic.Id.Value))
                .Where(guidance => guidance is not null)
                .Distinct(StringComparer.Ordinal)
                .Select(guidance => "Deterministic repair guidance: " + guidance));
        }

        return string.Join('\n', lines);
    }

    private static string? DiagnosticRepairGuidance(string diagnosticId) => diagnosticId switch
    {
        "CS8600" =>
            "Fix the declaration at the cited line, not a different occurrence. The expression " +
            "may return null: change the receiving reference type from T to T? and retain an " +
            "explicit null check, or use a semantically correct non-null fallback. In particular, " +
            "every Console.ReadLine() result must be received by string? rather than string.",
        "CS8602" =>
            "Prove the receiver is non-null with an explicit guard before dereference, or use a " +
            "null-safe operation whose fallback preserves the required behavior.",
        "CS8603" =>
            "The return expression may be null. Return a non-null value on every path or make the " +
            "declared return type nullable when null is part of the contract.",
        "CS8618" =>
            "Initialize the non-nullable member in every constructor or mark it required/nullable " +
            "only when that matches the public contract.",
        "CS0122" =>
            "The referenced member is non-public. Remove every call to it. Construct values only " +
            "through public constructors or factories shown in dependency context, then derive " +
            "needed states through public transition methods. Tests must never synthesize state " +
            "through a private storage constructor or private helper.",
        "xUnit2017" =>
            "Replace Assert.True(collection.Contains(expected)) with the analyzer-requested " +
            "Assert.Contains(expected, collection) form at each cited line; preserve surrounding " +
            "test setup and assertions.",
        _ => null,
    };

    private sealed record BootstrapInspection(
        string Context,
        string DependencyContext,
        string RelativePath,
        string Sha256,
        string? Content);

    private static string Name(AgentRole role) => role switch
    {
        AgentRole.Lead => "lead",
        AgentRole.Implementer => "implementer",
        AgentRole.Reviewer => "reviewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

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

    private static string Description(AgentRole role) => role switch
    {
        AgentRole.Lead => "Plans bounded work and coordinates specialist roles.",
        AgentRole.Implementer => "Implements an explicitly bounded task within the accepted architecture.",
        AgentRole.Reviewer => "Reviews evidence and identifies correctness, safety, and regression risks.",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static string Instructions(AgentRole role) => role switch
    {
        AgentRole.Lead =>
            "You are the lead agent. Inspect the workspace with typed tools before planning. " +
            "Turn the supplied objective into the smallest ordered set " +
            "of independently useful, verifiable slices. Front-load foundations and user-visible " +
            "value so stopping after any completed slice leaves a coherent partial result. Respect " +
            "every exact API, path, and prohibition in the objective. Use only repository paths you " +
            "observed; never invent files or directories. Use Roslyn symbol, definition, reference, implementation, " +
            "and diagnostic tools when code relationships affect the plan; do not infer semantic " +
            "facts from text matches alone. Identify explicit non-goals, and never " +
            "claim completion without evidence.",
        AgentRole.Implementer =>
            "You are the implementer agent. Complete only the supplied bounded task. Keep changes " +
            "narrow and the repository coherent at every durable tool boundary. The full goal objective " +
            "is authoritative even when the delegated plan paraphrases it. Your first action must be a " +
            "typed inspection tool call; do not narrate what you intend to do. Before editing, read the exact " +
            "existing target and pass its returned sha256 to apply_file_edit as expectedSha256; never " +
            "guess a path or hash. When the target consumes existing types, read their current definitions " +
            "and confirm exact signatures and accessibility with get_symbol_info and " +
            "find_symbol_definition; inspect usages or implementations when changing shared behavior " +
            "or abstractions. Use only APIs actually present and never " +
            "invent members or helper types. Use semantic rename for symbol renames instead of textual " +
            "replacement. Use the closed Roslyn document transformation preview/apply tools for C# " +
            "formatting, unused-import cleanup, or import organization instead of rewriting a file " +
            "for style alone. For an unresolved type, use missing-import discovery and apply only a " +
            "returned namespace through AddMissingImport. For compiler fixes and local refactorings, " +
            "call find_code_actions first and preview/apply only its returned ID and scope; prefer that " +
            "small semantic edit over regenerating a working file or method. Inspect " +
            "Roslyn problems before and after compiler-relevant changes. On a " +
            "diagnostic or test failure, preserve passing code and repair only the cited range or first " +
            "relevant user-code stack frame rather than regenerating the file. Treat a failed tool " +
            "result as actionable evidence: inspect, correct the " +
            "request, and retry with a new correlation identifier. Never submit TODO, FIXME, placeholder, " +
            "omitted, or NotImplementedException logic. A final response before at least one successful " +
            "mutation is a failed task, not a progress report. Prioritize the task's core acceptance " +
            "criteria, validate incrementally, and if execution must stop, preserve a " +
            "buildable useful partial result and report exactly what is complete, verified, and remaining.",
        AgentRole.Reviewer =>
            "You are the reviewer agent. Review the supplied work independently, including coherent " +
            "partial results. Prioritize correctness, regressions, boundary violations, missing tests, " +
            "and unsupported claims. Use Roslyn diagnostics, symbol information, definitions, usages, and " +
            "implementations to verify code claims rather than trusting text or the implementation report. " +
            "Distinguish verified completed value from unfinished scope.",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
