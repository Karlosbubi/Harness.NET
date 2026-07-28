using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Microsoft.Extensions.AI;

namespace Harness.BusinessLogic.Agents;

internal sealed class AgentToolFactory(
    IGoalWorkspaceInspectionService inspectionService,
    IWorkspaceMutationService mutationService,
    IToolEvidenceService evidenceService) : IAgentToolFactory
{
    public IList<AITool> Create(AgentRole role, GoalId goalId) =>
        AgentToolPolicy.AllowedFor(role)
            .Select(kind => Create(kind, goalId, role))
            .ToList();

    private AITool Create(AgentToolKind kind, GoalId goalId, AgentRole role) => kind switch
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
        AgentToolKind.ApplyFileEdit => AIFunctionFactory.Create(
            (string correlationId, string relativePath, string? expectedSha256, string content,
                    CancellationToken cancellationToken) =>
                mutationService.ApplyFileEditAsync(
                    new(goalId.Value, new ToolCorrelationId(correlationId), relativePath,
                        expectedSha256, content),
                    cancellationToken),
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

    private static AIFunctionFactoryOptions Options(string name, string description) => new()
    {
        Name = name,
        Description = description,
    };
}
