using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.VisualCapture;
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
    IDependencyResearchService? dependencyResearchService = null,
    IAgentToolActivationService? activationService = null,
    IChangedSetQualityService? qualityService = null,
    IInboundMcpUiBridge? uiBridge = null) : IAgentToolFactory
{
    public IList<AITool> Create(
        AgentRole role,
        GoalId goalId,
        IReadOnlyList<AgentFileArea> fileAreas,
        ModelAccess modelAccess = ModelAccess.Local)
    {
        IReadOnlySet<string> grants = activationService?.Consume(goalId, role) ?? new HashSet<string>();
        List<AITool> tools = AgentToolPolicy.AllowedFor(role)
            .Where(kind => activationService is null || IsExposed(kind, grants))
            .Where(kind => qualityService is not null || kind is not AgentToolKind.CheckChangedSetQuality)
            .Where(kind => uiBridge is not null || kind is not AgentToolKind.InspectOpenDocuments)
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
            tools.AddRange(mcpToolService.EligibleToolsFor(role).Select(tool =>
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
            AgentToolKind.ReadFileRange => AIFunctionFactory.Create(
                (string relativePath, int startLine, int lineCount,
                        CancellationToken cancellationToken) =>
                    inspectionService.ReadRangeAsync(
                        goalId, Scope(role), relativePath, startLine, lineCount,
                        cancellationToken),
                Options("read_file_range",
                    "Read 1-2000 one-based lines from one tracked file. Returns the complete-file " +
                    "sha256, exact source context, total lines, and whether more lines remain.")),
            AgentToolKind.ListWorkspaceTree => AIFunctionFactory.Create(
                (string relativeRoot, string? glob, int maximumDepth, int maximumResults,
                        string? continuation, CancellationToken cancellationToken) =>
                    inspectionService.ListTreeAsync(
                        goalId, Scope(role), relativeRoot, glob, maximumDepth, maximumResults,
                        continuation, cancellationToken),
                Options("list_workspace_tree",
                    "List a paged tracked-file tree below a relative root with an optional simple " +
                    "glob, depth 0-32, and page size 1-500. Reuse the returned continuation exactly.")),
            AgentToolKind.SearchText => AIFunctionFactory.Create(
                (string query, CancellationToken cancellationToken) =>
                    inspectionService.SearchTextAsync(
                        goalId, Scope(role), query, cancellationToken),
                Options("search_text", "Search tracked text in the goal workspace.")),
            AgentToolKind.SearchRegex => AIFunctionFactory.Create(
                (string pattern, string? fileGlob, int maximumResults, string? continuation,
                        CancellationToken cancellationToken) =>
                    inspectionService.SearchRegexAsync(
                        goalId, Scope(role), pattern, fileGlob, maximumResults, continuation,
                        cancellationToken),
                Options("search_regex",
                    "Search tracked UTF-8 text with a bounded regular expression and optional simple " +
                    "file glob. Returns one-based coordinates and a continuation for pages of 1-500.")),
            AgentToolKind.InspectGit => AIFunctionFactory.Create(
                (CancellationToken cancellationToken) =>
                    inspectionService.InspectGitAsync(goalId, Scope(role), cancellationToken),
                Options("inspect_git", "Inspect branch, status, and bounded diff for the goal workspace.")),
            AgentToolKind.InspectDotNet => AIFunctionFactory.Create(
                (CancellationToken cancellationToken) =>
                    inspectionService.InspectDotNetAsync(goalId, Scope(role), cancellationToken),
                Options("inspect_dotnet", "Inspect solution, project, SDK, and reference metadata.")),
            AgentToolKind.InspectProjectGraph => AIFunctionFactory.Create(
                (CancellationToken cancellationToken) =>
                    inspectionService.InspectProjectGraphAsync(
                        goalId, Scope(role), cancellationToken),
                Options("inspect_project_graph",
                    "Inspect the exact source context's bounded .NET projects, target frameworks, " +
                    "package references, and direct project dependency edges without restore.")),
            AgentToolKind.InspectOpenDocuments => AIFunctionFactory.Create(
                async (CancellationToken cancellationToken) =>
                    (await uiBridge!.InspectAsync(false, cancellationToken)).OpenDocuments,
                Options("inspect_open_documents",
                    "Inspect exact open editor buffers, paths, source goals, baselines, dirty state, buffer versions, and active selection. Returns no hidden desktop state.")),
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
            AgentToolKind.InspectProjectProblems => AIFunctionFactory.Create(
                (int maximumFiles, CancellationToken cancellationToken) =>
                    codeIntelligenceService.InspectProjectProblemsAsync(
                        goalId, Scope(role), maximumFiles, cancellationToken),
                Options("inspect_project_problems",
                    "Inspect Roslyn diagnostics across 1-100 tracked C# files with exact source " +
                    "identity, bounded diagnostics, truncation, and continuation.")),
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
                    "Resolve the definition of the symbol at a zero-based line and character with " +
                    "Roslyn. Generated and metadata results eagerly include bounded read-only source; " +
                    "metadata source contains locally reconstructed method bodies when available. " +
                    "Read it before editing dependent code.")),
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
            AgentToolKind.InspectCode => AIFunctionFactory.Create(
                (string relativePath, int line, int character, WorkbenchCodeInspectionKind kind,
                        CancellationToken cancellationToken) => codeIntelligenceService.InspectAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), kind,
                    cancellationToken),
                Options("inspect_code",
                    "Return one exact-context read-only SyntaxTree, Symbol, GeneratedSource, or " +
                    "IntermediateLanguage view. Results name the project, target framework, " +
                    "configuration, assembly, document version, and compilation identity.")),
            AgentToolKind.SearchSymbols => AIFunctionFactory.Create(
                (string relativePath, string query, int maximumResults, int offset,
                        CancellationToken cancellationToken) => codeIntelligenceService.SearchSymbolsAsync(
                    goalId, Scope(role), new(relativePath), query, maximumResults, offset, cancellationToken),
                Options("search_symbols", "Search Roslyn declarations by name in the exact source context. " +
                    "Use 1-200 results and reuse the numeric continuation as offset.")),
            AgentToolKind.AnalyzeCalls => AIFunctionFactory.Create(
                (string relativePath, int line, int character, int maximumResults, int offset,
                        CancellationToken cancellationToken) => codeIntelligenceService.AnalyzeCallsAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), maximumResults, offset,
                    cancellationToken),
                Options("analyze_calls", "Find bounded incoming and outgoing calls for the symbol at a " +
                    "zero-based source position. Reuse the numeric continuation as offset.")),
            AgentToolKind.GetTypeHierarchy => AIFunctionFactory.Create(
                (string relativePath, int line, int character, int maximumResults, int offset,
                        CancellationToken cancellationToken) => codeIntelligenceService.GetTypeHierarchyAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), maximumResults, offset,
                    cancellationToken),
                Options("get_type_hierarchy", "Find bounded base, interface, derived, and override relations " +
                    "for the symbol at a zero-based source position.")),
            AgentToolKind.FindAssociatedTests => AIFunctionFactory.Create(
                (string relativePath, int line, int character, int maximumResults, int offset,
                        CancellationToken cancellationToken) => codeIntelligenceService.FindAssociatedTestsAsync(
                    goalId, Scope(role), new(relativePath), new(line, character), maximumResults, offset,
                    cancellationToken),
                Options("find_associated_tests", "Find deterministic test-project or attributed-test usages " +
                    "associated with the symbol at a zero-based source position.")),
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
            AgentToolKind.FindMissingImports => AIFunctionFactory.Create(
                (string relativePath, int line, int character,
                        CancellationToken cancellationToken) =>
                    codeIntelligenceService.FindMissingImportsAsync(
                        goalId, Scope(role), new(relativePath), new(line, character),
                        cancellationToken),
                Options("find_missing_imports",
                    "Find exact namespace candidates that Roslyn proves bind the unresolved type at " +
                    "the zero-based caret. Use the selected namespace with AddMissingImport preview.")),
            AgentToolKind.FindCodeActions => AIFunctionFactory.Create(
                (string relativePath, int line, int character,
                        int? startLine = null, int? startCharacter = null,
                        int? endLine = null, int? endCharacter = null,
                        CancellationToken cancellationToken = default) =>
                    codeIntelligenceService.FindCodeActionsAsync(
                        goalId, Scope(role), new(relativePath), new(line, character),
                        OptionalRange(startLine, startCharacter, endLine, endCharacter),
                        cancellationToken: cancellationToken),
                Options("find_code_actions",
                    "Find closed Roslyn quick fixes and refactorings at the zero-based caret or " +
                    "optional exact selection. Supply all four selection coordinates or none. Use the returned " +
                    "code-action ID and scope with ApplyCodeAction preview; arbitrary actions are rejected.")),
            AgentToolKind.PreviewDocumentTransformation => AIFunctionFactory.Create(
                (string relativePath, string expectedSha256, string content,
                        WorkbenchCodeDocumentTransformationKind kind,
                        int? line = null, int? character = null,
                        int? startLine = null, int? startCharacter = null,
                        int? endLine = null, int? endCharacter = null,
                        string? importNamespace = null,
                        WorkbenchCodeFormattingTrigger? formattingTrigger = null,
                        string? codeActionId = null,
                        WorkbenchCodeActionScope? codeActionScope = null,
                        CancellationToken cancellationToken = default) =>
                    mutationService.PreviewDocumentTransformationAsync(
                        DocumentTransformationRequest(
                            goalId, relativePath, expectedSha256, content, kind,
                            startLine, startCharacter, endLine, endCharacter, importNamespace,
                            formattingTrigger, line, character, codeActionId, codeActionScope,
                            fileAreas),
                        cancellationToken),
                Options("preview_document_transformation",
                    "Preview one closed Roslyn operation: FormatDocument, FormatSelection, " +
                    "FormatChangedSpans, FormatPaste, FormatOnType, OrganizeImports, " +
                    "RemoveUnusedImports, AddMissingImport, or ApplyCodeAction. Pass all four zero-based coordinates " +
                    "for selection, paste, or on-type formatting. Paste and on-type also require their " +
                    "matching typed trigger. For AddMissingImport, " +
                    "pass a namespace returned by find_missing_imports. ApplyCodeAction requires a " +
                    "zero-based line and character plus the ID and scope from find_code_actions. " +
                    "Approved cross-document actions return every affected file. Returns exact edit evidence " +
                    "and a fingerprint; it changes nothing.")),
            AgentToolKind.ApplyDocumentTransformation => AIFunctionFactory.Create(
                (string correlationId, string fingerprint, string relativePath,
                        string expectedSha256, string content,
                        WorkbenchCodeDocumentTransformationKind kind,
                        int? line = null, int? character = null,
                        int? startLine = null, int? startCharacter = null,
                        int? endLine = null, int? endCharacter = null,
                        string? importNamespace = null,
                        WorkbenchCodeFormattingTrigger? formattingTrigger = null,
                        string? codeActionId = null,
                        WorkbenchCodeActionScope? codeActionScope = null,
                        CancellationToken cancellationToken = default) =>
                    mutationService.ApplyDocumentTransformationAsync(new(
                        DocumentTransformationRequest(
                            goalId, relativePath, expectedSha256, content, kind,
                            startLine, startCharacter, endLine, endCharacter, importNamespace,
                            formattingTrigger, line, character, codeActionId, codeActionScope,
                            fileAreas),
                        new ToolCorrelationId(correlationId),
                        new(fingerprint)), cancellationToken),
                Options("apply_document_transformation",
                    "Recompute and atomically apply every file in an accepted closed Roslyn preview " +
                    "by exact fingerprint, then run Roslyn post-validation.")),
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
            AgentToolKind.DiscoverToolsets => AIFunctionFactory.Create(
                () => AgentToolCatalog.Default.Modules.Where(module =>
                    module.Roles.Contains(role) && module.Availability is AgentToolModuleAvailability.Available)
                    .Select(module => new
                    {
                        id = module.Id.Value,
                        module.DisplayName,
                        module.Summary,
                        exposure = module.Exposure.ToString(),
                        authority = module.Authority.ToString(),
                        operations = module.Operations.Select(operation => operation.Value).ToArray()
                    })
                    .ToArray(),
                Options("discover_toolsets",
                    "List closed available Harness IDE toolsets for this role. Discovery invokes nothing and grants no authority.")),
            AgentToolKind.RequestToolset => AIFunctionFactory.Create(
                (string moduleId, CancellationToken cancellationToken) =>
                    activationService is null
                        ? ValueTask.FromResult(new AgentToolsetRequestResult(new(moduleId), false,
                            "toolset_activation_unavailable", "On-demand activation is unavailable."))
                        : activationService.RequestAsync(goalId, role, new(moduleId), cancellationToken),
                Options("request_toolset",
                    "Request one discovered on-demand typed toolset for the next bounded role turn. This invokes no operation and grants no new authority.")),
            AgentToolKind.CheckChangedSetQuality => AIFunctionFactory.Create(
                (CancellationToken cancellationToken) => qualityService!.EvaluateAsync(goalId, cancellationToken),
                Options("post_edit_quality_check",
                    "Run one deterministic changed-set gate over Roslyn diagnostics, placeholders, and current Build/Test evidence. It never self-certifies model output.")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static bool IsExposed(AgentToolKind kind, IReadOnlySet<string> grants)
    {
        if (kind is AgentToolKind.DiscoverToolsets or AgentToolKind.RequestToolset) return true;
        string operation = OperationName(kind);
        AgentToolModule? module = AgentToolCatalog.Default.Modules.FirstOrDefault(candidate =>
            candidate.Operations.Any(item => item.Value.Equals(operation, StringComparison.Ordinal)));
        return module is null || module.Exposure is AgentToolExposure.Direct || grants.Contains(module.Id.Value);
    }

    private static string OperationName(AgentToolKind kind) => kind switch
    {
        AgentToolKind.ReadFile => "read_file",
        AgentToolKind.ReadFileRange => "read_file_range",
        AgentToolKind.ListWorkspaceTree => "list_workspace_tree",
        AgentToolKind.SearchText => "search_text",
        AgentToolKind.SearchRegex => "search_regex",
        AgentToolKind.InspectGit => "inspect_git",
        AgentToolKind.InspectDotNet => "inspect_dotnet",
        AgentToolKind.InspectProjectGraph => "inspect_project_graph",
        AgentToolKind.InspectOpenDocuments => "inspect_open_documents",
        AgentToolKind.LookupDocumentation => "lookup_documentation",
        AgentToolKind.InspectDependencies => "inspect_dependencies",
        AgentToolKind.ValidatePackageCandidate => "validate_package_candidate",
        AgentToolKind.PreviewSbom => "preview_sbom",
        AgentToolKind.PreviewPackageChange => "preview_package_change",
        AgentToolKind.InspectCodeProblems => "inspect_code_problems",
        AgentToolKind.InspectProjectProblems => "inspect_project_problems",
        AgentToolKind.GetSymbolInfo => "get_symbol_info",
        AgentToolKind.FindDefinition => "find_symbol_definition",
        AgentToolKind.FindReferences => "find_symbol_references",
        AgentToolKind.FindImplementations => "find_symbol_implementations",
        AgentToolKind.InspectCode => "inspect_code",
        AgentToolKind.SearchSymbols => "search_symbols",
        AgentToolKind.AnalyzeCalls => "analyze_calls",
        AgentToolKind.GetTypeHierarchy => "get_type_hierarchy",
        AgentToolKind.FindAssociatedTests => "find_associated_tests",
        AgentToolKind.CheckChangedSetQuality => "post_edit_quality_check",
        AgentToolKind.ApplyFileEdit => "apply_file_edit",
        AgentToolKind.PreviewRename => "preview_symbol_rename",
        AgentToolKind.ApplyRename => "apply_symbol_rename",
        AgentToolKind.FindMissingImports => "find_missing_imports",
        AgentToolKind.FindCodeActions => "find_code_actions",
        AgentToolKind.PreviewDocumentTransformation => "preview_document_transformation",
        AgentToolKind.ApplyDocumentTransformation => "apply_document_transformation",
        AgentToolKind.Build => "dotnet_build",
        AgentToolKind.Test => "dotnet_test",
        AgentToolKind.RequestVisualCapture => "request_visual_capture",
        AgentToolKind.InspectVisualCapture => "inspect_visual_capture",
        _ => kind.ToString(),
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

    private static DocumentTransformationPreviewRequest DocumentTransformationRequest(
        GoalId goalId,
        string relativePath,
        string expectedSha256,
        string content,
        WorkbenchCodeDocumentTransformationKind kind,
        int? startLine,
        int? startCharacter,
        int? endLine,
        int? endCharacter,
        string? importNamespace,
        WorkbenchCodeFormattingTrigger? formattingTrigger,
        int? line,
        int? character,
        string? codeActionId,
        WorkbenchCodeActionScope? codeActionScope,
        IReadOnlyList<AgentFileArea> fileAreas)
    {
        bool hasAnyRange = startLine is not null || startCharacter is not null ||
            endLine is not null || endCharacter is not null;
        bool hasCompleteRange = startLine is not null && startCharacter is not null &&
            endLine is not null && endCharacter is not null;
        WorkbenchCodeRange? range = hasCompleteRange
            ? new(
                new(startLine!.Value, startCharacter!.Value),
                new(endLine!.Value, endCharacter!.Value))
            : hasAnyRange
                ? new(new(-1, -1), new(-1, -1))
                : null;
        return new(
            goalId.Value,
            new(relativePath),
            new(expectedSha256),
            new(1),
            new(content),
            new(line ?? range?.Start.Line ?? 0, character ?? range?.Start.Character ?? 0),
            kind,
            range,
            DocumentTransformationOrigin.Model,
            fileAreas.Select(area => new DocumentTransformationFileArea(area.Value)).ToArray(),
            string.IsNullOrWhiteSpace(importNamespace) ? null : new(importNamespace),
            formattingTrigger,
            string.IsNullOrWhiteSpace(codeActionId) ? null : new(codeActionId),
            codeActionScope);
    }

    private static WorkbenchCodeRange? OptionalRange(
        int? startLine,
        int? startCharacter,
        int? endLine,
        int? endCharacter)
    {
        bool any = startLine is not null || startCharacter is not null ||
            endLine is not null || endCharacter is not null;
        bool complete = startLine is not null && startCharacter is not null &&
            endLine is not null && endCharacter is not null;
        return complete
            ? new(new(startLine!.Value, startCharacter!.Value),
                new(endLine!.Value, endCharacter!.Value))
            : any
                ? new(new(-1, -1), new(-1, -1))
                : null;
    }

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
