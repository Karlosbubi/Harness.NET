namespace Harness.BusinessLogic.Agents;

internal static class AgentToolPolicy
{
    internal static IReadOnlyList<AgentToolKind> AllowedFor(AgentRole role) => role switch
    {
        AgentRole.Lead =>
        [
            AgentToolKind.ReadFile,
            AgentToolKind.SearchText,
            AgentToolKind.InspectGit,
            AgentToolKind.InspectDotNet,
            AgentToolKind.SemanticContext,
        ],
        AgentRole.Implementer =>
        [
            AgentToolKind.ReadFile,
            AgentToolKind.SearchText,
            AgentToolKind.InspectGit,
            AgentToolKind.InspectDotNet,
            AgentToolKind.SemanticContext,
            AgentToolKind.ApplyFileEdit,
            AgentToolKind.Build,
            AgentToolKind.Test,
        ],
        AgentRole.Reviewer =>
        [
            AgentToolKind.ReadFile,
            AgentToolKind.SearchText,
            AgentToolKind.InspectGit,
            AgentToolKind.InspectDotNet,
            AgentToolKind.SemanticContext,
            AgentToolKind.ListEvidence,
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
