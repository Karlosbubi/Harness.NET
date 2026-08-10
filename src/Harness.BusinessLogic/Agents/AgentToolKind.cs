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
    ApplyFileEdit,
    PreviewRename,
    ApplyRename,
    Build,
    Test,
    ListEvidence,
}
