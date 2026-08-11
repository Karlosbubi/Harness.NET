namespace Harness.BusinessLogic.Agents;

internal enum AgentToolKind
{
    ReadFile,
    SearchText,
    InspectGit,
    InspectDotNet,
  LookupDocumentation,
  InspectDependencies,
  ValidatePackageCandidate,
  PreviewSbom,
  PreviewPackageChange,
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
    RequestVisualCapture,
    InspectVisualCapture,
}
