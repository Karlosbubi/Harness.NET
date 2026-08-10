using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Tools;
using Microsoft.Extensions.AI;

namespace Harness.BusinessLogic.Agents;

internal sealed class AgentToolFactory(
    IGoalWorkspaceInspectionService inspectionService,
    IWorkspaceMutationService mutationService,
    IToolEvidenceService evidenceService,
    IGoalContextService contextService,
    IGoalCodeIntelligenceService codeIntelligenceService,
    IMcpToolService? mcpToolService = null) : IAgentToolFactory
{
    public IList<AITool> Create(
        AgentRole role,
        GoalId goalId,
        IReadOnlyList<AgentFileArea> fileAreas)
    {
        List<AITool> tools = AgentToolPolicy.AllowedFor(role)
            .Select(kind => Create(kind, goalId, role, fileAreas))
            .ToList();
        if (mcpToolService is not null)
        {
            tools.AddRange(mcpToolService.EligibleTools.Select(tool =>
                (AITool)new McpAgentFunction(tool, mcpToolService)));
        }
        return tools;
    }

    private AITool Create(
        AgentToolKind kind,
        GoalId goalId,
        AgentRole role,
        IReadOnlyList<AgentFileArea> fileAreas) => kind switch
    {
        AgentToolKind.ReadFile => AIFunctionFactory.Create(
            (string relativePath, CancellationToken cancellationToken) =>
                inspectionService.ReadFileAsync(
                    goalId, Scope(role), relativePath, cancellationToken),
            Options("read_file", "Read one bounded existing file from the goal workspace. " +
                "A complete read returns sha256; pass that exact value as expectedSha256 " +
                "when replacing the file with apply_file_edit.")),
        AgentToolKind.SearchText => AIFunctionFactory.Create(
            (string query, CancellationToken cancellationToken) =>
                inspectionService.SearchTextAsync(
                    goalId, Scope(role), query, cancellationToken),
            Options("search_text", "Search tracked text in the goal workspace.")),
        AgentToolKind.InspectGit => AIFunctionFactory.Create(
            (CancellationToken cancellationToken) =>
                inspectionService.InspectGitAsync(goalId, Scope(role), cancellationToken),
            Options("inspect_git", "Inspect branch, status, and bounded diff for the goal workspace.")),
        AgentToolKind.InspectDotNet => AIFunctionFactory.Create(
            (CancellationToken cancellationToken) =>
                inspectionService.InspectDotNetAsync(goalId, Scope(role), cancellationToken),
            Options("inspect_dotnet", "Inspect solution, project, SDK, and reference metadata.")),
        AgentToolKind.SemanticContext => AIFunctionFactory.Create(
            (string query, int maximumResults, CancellationToken cancellationToken) =>
                contextService.SearchAsync(
                    new(goalId, new(query), new(maximumResults)), cancellationToken),
            Options("search_semantic_context",
                "Retrieve 1-8 relevant bounded chunks from the compatible semantic index. " +
                "Remote embeddings are separately cost-attributed to this goal.")),
        AgentToolKind.InspectCodeProblems => AIFunctionFactory.Create(
            (string relativePath, CancellationToken cancellationToken) =>
                codeIntelligenceService.InspectProblemsAsync(
                    goalId, Scope(role), new(relativePath), cancellationToken),
            Options("inspect_code_problems",
                "Ask Roslyn for current compiler diagnostics in one complete source file. " +
                "The file and exact baseline are loaded from the role's source context.")),
        AgentToolKind.GetSymbolInfo => AIFunctionFactory.Create(
            (string relativePath, int line, int character, CancellationToken cancellationToken) =>
                codeIntelligenceService.GetSymbolAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), cancellationToken),
            Options("get_symbol_info",
                "Ask Roslyn for the symbol at a zero-based line and character in a source file.")),
        AgentToolKind.FindDefinition => AIFunctionFactory.Create(
            (string relativePath, int line, int character, CancellationToken cancellationToken) =>
                codeIntelligenceService.FindDefinitionAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), cancellationToken),
            Options("find_symbol_definition",
                "Resolve the definition of the symbol at a zero-based line and character with Roslyn.")),
        AgentToolKind.FindReferences => AIFunctionFactory.Create(
            (string relativePath, int line, int character, CancellationToken cancellationToken) =>
                codeIntelligenceService.FindReferencesAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), cancellationToken),
            Options("find_symbol_references",
                "Find bounded references to the symbol at a zero-based line and character with Roslyn.")),
        AgentToolKind.FindImplementations => AIFunctionFactory.Create(
            (string relativePath, int line, int character, CancellationToken cancellationToken) =>
                codeIntelligenceService.FindImplementationsAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), cancellationToken),
            Options("find_symbol_implementations",
                "Find bounded source implementations of the symbol at a zero-based line and character with Roslyn.")),
        AgentToolKind.ApplyFileEdit => AIFunctionFactory.Create(
            (string correlationId, string relativePath, string? expectedSha256, string content,
                    CancellationToken cancellationToken) =>
                IsWithinFileAreas(relativePath, fileAreas)
                    ? mutationService.ApplyFileEditAsync(
                        new(goalId.Value, new ToolCorrelationId(correlationId), relativePath,
                            expectedSha256, content, FileEditOrigin.Model),
                        cancellationToken)
                    : ValueTask.FromResult(new FileEditView(
                        goalId.Value,
                        new(correlationId),
                        relativePath,
                        PreviousSha256: null,
                        NewSha256: null,
                        BytesWritten: 0,
                        WasCreated: false,
                        "task_file_area_denied",
                        "The delegated task does not authorize edits in this file area.")),
            Options("apply_file_edit",
                "Validate and atomically replace one file in the approved goal worktree. " +
                "For C#, project, solution, props, or targets files, first call read_file on " +
                "the exact existing path and pass its sha256 as expectedSha256. Do not invent " +
                "compiler-input paths; new compiler files are rejected without a baseline. " +
                "Submit complete production code: TODO, FIXME, placeholder or omitted logic, " +
                "and NotImplementedException are deterministically rejected.")),
        AgentToolKind.PreviewRename => AIFunctionFactory.Create(
            (string relativePath, string expectedSha256, string content, int line, int character,
                    string newName, CancellationToken cancellationToken) =>
                mutationService.PreviewRenameAsync(
                    RenameRequest(goalId, relativePath, expectedSha256, content, line, character,
                        newName, fileAreas),
                    cancellationToken),
            Options("preview_symbol_rename",
                "Resolve a Roslyn symbol rename and return its complete bounded preview and fingerprint.")),
        AgentToolKind.ApplyRename => AIFunctionFactory.Create(
            (string correlationId, string fingerprint, string relativePath, string expectedSha256,
                    string content, int line, int character, string newName,
                    CancellationToken cancellationToken) =>
                mutationService.ApplyRenameAsync(new(
                    RenameRequest(goalId, relativePath, expectedSha256, content, line, character,
                        newName, fileAreas),
                    new ToolCorrelationId(correlationId),
                    new(fingerprint)), cancellationToken),
            Options("apply_symbol_rename",
                "Recompute and atomically apply an accepted Roslyn rename preview by fingerprint.")),
        AgentToolKind.Build => AIFunctionFactory.Create(
            (string correlationId, CancellationToken cancellationToken) =>
                mutationService.RunDotNetAsync(
                    new(goalId.Value, new ToolCorrelationId(correlationId), DotNetOperation.Build),
                    cancellationToken),
            Options("dotnet_build", "Build the approved goal entry point without restoring packages.")),
        AgentToolKind.Test => AIFunctionFactory.Create(
            (string correlationId, CancellationToken cancellationToken) =>
                mutationService.RunDotNetAsync(
                    new(goalId.Value, new ToolCorrelationId(correlationId), DotNetOperation.Test),
                    cancellationToken),
            Options("dotnet_test", "Test the approved goal entry point without restoring packages.")),
        AgentToolKind.ListEvidence => AIFunctionFactory.Create(
            (CancellationToken cancellationToken) =>
                evidenceService.ListAsync(goalId.Value, cancellationToken),
            Options("list_tool_evidence", "List durable mutation and verification evidence for the goal.")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static GoalWorkspaceScope Scope(AgentRole role) =>
        role is AgentRole.Lead
            ? GoalWorkspaceScope.Original
            : GoalWorkspaceScope.ApprovedWorktree;

    private static RenameSymbolPreviewRequest RenameRequest(
        GoalId goalId,
        string relativePath,
        string expectedSha256,
        string content,
        int line,
        int character,
        string newName,
        IReadOnlyList<AgentFileArea> fileAreas) => new(
        goalId.Value,
        new(relativePath),
        new(expectedSha256),
        new(1),
        new(content),
        new(line, character),
        new(newName),
        RenameSymbolOrigin.Model,
        fileAreas.Select(area => new RenameFileArea(area.Value)).ToArray());

    internal static bool IsWithinFileAreas(
        string relativePath,
        IReadOnlyList<AgentFileArea> fileAreas)
    {
        if (!ValidFileArea(relativePath))
        {
            return false;
        }

        string path = Normalize(relativePath);
        return fileAreas.Any(area =>
        {
            string allowed = Normalize(area.Value);
            return path.Equals(allowed, StringComparison.Ordinal) ||
                path.StartsWith(allowed + "/", StringComparison.Ordinal);
        });
    }

    internal static bool ValidFileArea(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
            value.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        string normalized = Normalize(value);
        return normalized.Length > 0 && normalized.Length <= 512 &&
            normalized.Split('/').All(segment =>
                segment.Length > 0 && segment is not "." and not "..");
    }

    private static string Normalize(string value) => value.Trim().TrimEnd('/');

    private static AIFunctionFactoryOptions Options(string name, string description) => new()
    {
        Name = name,
        Description = description,
    };
}
