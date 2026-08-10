namespace Harness.BusinessLogic.Agents;

internal enum AgentToolKind
{
    ReadFile,
    SearchText,
    InspectGit,
    InspectDotNet,
    SemanticContext,
    InspectCodeProblems,
    GetSymbolInfo,
    FindDefinition,
    FindReferences,
    FindImplementations,
    ApplyFileEdit,
    PreviewRename,
    ApplyRename,
    Build,
    Test,
    ListEvidence,
}
