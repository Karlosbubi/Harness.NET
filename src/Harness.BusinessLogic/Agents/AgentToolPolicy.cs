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
            AgentToolKind.InspectCodeProblems,
            AgentToolKind.GetSymbolInfo,
            AgentToolKind.FindDefinition,
            AgentToolKind.FindReferences,
            AgentToolKind.FindImplementations,
        ],
        AgentRole.Implementer =>
        [
            AgentToolKind.ReadFile,
            AgentToolKind.SearchText,
            AgentToolKind.InspectGit,
            AgentToolKind.InspectDotNet,
            AgentToolKind.SemanticContext,
            AgentToolKind.InspectCodeProblems,
            AgentToolKind.GetSymbolInfo,
            AgentToolKind.FindDefinition,
            AgentToolKind.FindReferences,
            AgentToolKind.FindImplementations,
            AgentToolKind.ApplyFileEdit,
            AgentToolKind.PreviewRename,
            AgentToolKind.ApplyRename,
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
            AgentToolKind.InspectCodeProblems,
            AgentToolKind.GetSymbolInfo,
            AgentToolKind.FindDefinition,
            AgentToolKind.FindReferences,
            AgentToolKind.FindImplementations,
            AgentToolKind.ListEvidence,
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
