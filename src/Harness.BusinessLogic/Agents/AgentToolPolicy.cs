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
            AgentToolKind.RequestVisualCapture,
            AgentToolKind.InspectVisualCapture,
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
            AgentToolKind.RequestVisualCapture,
            AgentToolKind.InspectVisualCapture,
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
            AgentToolKind.RequestVisualCapture,
            AgentToolKind.InspectVisualCapture,
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
