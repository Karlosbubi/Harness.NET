using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Microsoft.Extensions.AI;

namespace Harness.BusinessLogic.Agents;

internal sealed class AgentToolFactory(
    IGoalWorkspaceInspectionService inspectionService,
    IWorkspaceMutationService mutationService,
    IToolEvidenceService evidenceService,
    IGoalContextService contextService) : IAgentToolFactory
{
    public IList<AITool> Create(
        AgentRole role,
        GoalId goalId,
        IReadOnlyList<AgentFileArea> fileAreas) =>
        AgentToolPolicy.AllowedFor(role)
            .Select(kind => Create(kind, goalId, role, fileAreas))
            .ToList();

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
            Options("read_file", "Read one bounded file from the goal workspace.")),
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
        AgentToolKind.ApplyFileEdit => AIFunctionFactory.Create(
            (string correlationId, string relativePath, string? expectedSha256, string content,
                    CancellationToken cancellationToken) =>
                IsWithinFileAreas(relativePath, fileAreas)
                    ? mutationService.ApplyFileEditAsync(
                        new(goalId.Value, new ToolCorrelationId(correlationId), relativePath,
                            expectedSha256, content),
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
                "Apply one atomic file replacement in the approved goal worktree.")),
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
