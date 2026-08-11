using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Research;
using Microsoft.Extensions.AI;

namespace Harness.BusinessLogic.Agents;

internal sealed class AgentToolFactory(
    IGoalWorkspaceInspectionService inspectionService,
    IWorkspaceMutationService mutationService,
    IToolEvidenceService evidenceService,
    IGoalContextService contextService,
    IGoalCodeIntelligenceService codeIntelligenceService,
    IMcpToolService? mcpToolService = null,
    IVisualCaptureService? visualCaptureService = null,
    TimeProvider? timeProvider = null,
    IDocumentationResearchService? documentationResearchService = null,
    IDependencyResearchService? dependencyResearchService = null) : IAgentToolFactory
{
    public IList<AITool> Create(
        AgentRole role,
        GoalId goalId,
        IReadOnlyList<AgentFileArea> fileAreas,
        ModelAccess modelAccess = ModelAccess.Local)
    {
        List<AITool> tools = AgentToolPolicy.AllowedFor(role)
            .Where(kind => visualCaptureService is not null ||
                kind is not AgentToolKind.RequestVisualCapture and not AgentToolKind.InspectVisualCapture)
        .Where(kind => documentationResearchService is not null ||
            kind is not AgentToolKind.LookupDocumentation)
        .Where(kind => dependencyResearchService is not null || kind is not
            (AgentToolKind.InspectDependencies or AgentToolKind.ValidatePackageCandidate or
             AgentToolKind.PreviewSbom or AgentToolKind.PreviewPackageChange))
            .Select(kind => Create(kind, goalId, role, fileAreas, modelAccess))
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
        IReadOnlyList<AgentFileArea> fileAreas,
        ModelAccess modelAccess) => kind switch
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
        AgentToolKind.LookupDocumentation => AIFunctionFactory.Create(
              (string library, string? version, string question, CancellationToken cancellationToken) =>
                  documentationResearchService!.LookupAsync(new(
                      goalId,
                      new(library),
                      string.IsNullOrWhiteSpace(version) ? null : new(version),
                      new(question)), cancellationToken),
              Options("lookup_documentation",
                  "Look up version-matched library documentation on demand. Returns a small ranked " +
                  "set with source, version, freshness, confidence, citation, and escalation history. " +
                  "Use this before guessing an external API; do not call it for facts already established.")),
        AgentToolKind.InspectDependencies => AIFunctionFactory.Create(
              (CancellationToken cancellationToken) => dependencyResearchService!.InspectAsync(
                  new(goalId, DependencyScope(role)), cancellationToken),
              Options("inspect_dependencies",
                  "Read declared, central, direct, transitive, and already-restored package evidence " +
                  "without restoring or executing project targets.")),
        AgentToolKind.ValidatePackageCandidate => AIFunctionFactory.Create(
              (string packageId, string version, bool allowPrerelease,
                      CancellationToken cancellationToken) =>
                  dependencyResearchService!.ValidateCandidateAsync(new(
                      goalId, new(packageId), new(version), allowPrerelease, DependencyScope(role)),
                      cancellationToken),
              Options("validate_package_candidate",
                  "Validate one exact package version against configured sources. Reports framework " +
                  "compatibility, transitive ranges, listing/deprecation, advisories, license, " +
                  "provenance, and integrity. This performs no restore or mutation.")),
        AgentToolKind.PreviewSbom => AIFunctionFactory.Create(
              (CancellationToken cancellationToken) => dependencyResearchService!.PreviewSbomAsync(
                  new(goalId, DependencyScope(role)), cancellationToken),
              Options("preview_sbom",
                  "Generate deterministic CycloneDX 1.6 JSON from the existing restored graph. " +
                  "This does not export or change repository files.")),
        AgentToolKind.PreviewPackageChange => AIFunctionFactory.Create(
              (string packageId, string version, bool allowPrerelease,
                      CancellationToken cancellationToken) =>
                  dependencyResearchService!.PreviewPackageChangeAsync(new(
                      goalId, new(packageId), new(version), allowPrerelease, DependencyScope(role)),
                      cancellationToken),
              Options("preview_package_change",
                  "Validate one exact package candidate and show dependency and deterministic SBOM " +
                  "diffs before any separately authorized project mutation.")),
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
                "The file and exact baseline are loaded from the role's source context. " +
                "Use cited ranges to repair the smallest affected span rather than rewriting " +
                "unrelated declarations.")),
        AgentToolKind.GetSymbolInfo => AIFunctionFactory.Create(
            (string relativePath, int line, int character, CancellationToken cancellationToken) =>
                codeIntelligenceService.GetSymbolAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), cancellationToken),
            Options("get_symbol_info",
                "Ask Roslyn for the exact signature, accessibility, documentation, and destination " +
                "of the symbol at a zero-based line and character. Call this before consuming or " +
                "changing an API instead of guessing its members.")),
        AgentToolKind.FindDefinition => AIFunctionFactory.Create(
            (string relativePath, int line, int character, CancellationToken cancellationToken) =>
                codeIntelligenceService.FindDefinitionAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), cancellationToken),
            Options("find_symbol_definition",
                "Resolve the source definition of the symbol at a zero-based line and character " +
                "with Roslyn. Read the returned definition before editing dependent code.")),
        AgentToolKind.FindReferences => AIFunctionFactory.Create(
            (string relativePath, int line, int character, CancellationToken cancellationToken) =>
                codeIntelligenceService.FindReferencesAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), cancellationToken),
            Options("find_symbol_references",
                "Find bounded usages of the symbol at a zero-based line and character with Roslyn. " +
                "Use this before changing behavior shared by existing consumers.")),
        AgentToolKind.FindImplementations => AIFunctionFactory.Create(
            (string relativePath, int line, int character, CancellationToken cancellationToken) =>
                codeIntelligenceService.FindImplementationsAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), cancellationToken),
            Options("find_symbol_implementations",
                "Find bounded source implementations and overrides of the symbol at a zero-based " +
                "line and character with Roslyn. Inspect them before changing an abstraction.")),
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
                "Preserve working regions and prefer the smallest coherent change; use Roslyn " +
                "symbol/navigation results rather than inventing signatures or accessibility. " +
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
        AgentToolKind.RequestVisualCapture => AIFunctionFactory.Create(
            (string correlationId, string relatedAction, VisualCaptureTarget target,
                    CancellationToken cancellationToken) =>
                visualCaptureService!.CaptureAsync(new(
                    goalId,
                    new ToolCorrelationId(correlationId),
                    Initiator(role),
                    new(relatedAction),
                    new("Harness.NET"),
                    target,
                    (timeProvider ?? TimeProvider.System).GetUtcNow()), cancellationToken),
            Options("request_visual_capture",
                "Ask the user to approve one interactive XDG Desktop Portal screenshot for a specific verification action. No frame is captured without portal consent.")),
        AgentToolKind.InspectVisualCapture => AIFunctionFactory.Create(
            (string captureId, CancellationToken cancellationToken) =>
                visualCaptureService!.InspectAsync(
                    goalId,
                    new(captureId),
                    modelAccess is ModelAccess.Remote
                        ? VisualCaptureModelAccess.Remote
                        : VisualCaptureModelAccess.Local,
                    cancellationToken),
            Options("inspect_visual_capture",
                "Inspect the exact stored bytes and metadata of one goal-scoped capture. Remote disclosure must be enabled explicitly in Settings.")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static GoalWorkspaceScope Scope(AgentRole role) =>
        role is AgentRole.Lead
            ? GoalWorkspaceScope.Original
            : GoalWorkspaceScope.ApprovedWorktree;

  private static DependencyInspectionScope DependencyScope(AgentRole role) =>
      role is AgentRole.Lead
          ? DependencyInspectionScope.Original
          : DependencyInspectionScope.ApprovedWorktree;

    private static VisualCaptureInitiator Initiator(AgentRole role) => role switch
    {
        AgentRole.Lead => VisualCaptureInitiator.Lead,
        AgentRole.Implementer => VisualCaptureInitiator.Implementer,
        AgentRole.Reviewer => VisualCaptureInitiator.Reviewer,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

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
