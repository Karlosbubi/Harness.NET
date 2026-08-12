using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.Retrieval;
using Harness.DataAccess.Mcp;
using Microsoft.Extensions.AI;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class AgentToolPolicyTests
{
    [Fact]
    public void Lead_is_read_only()
    {
        IReadOnlyList<AgentToolKind> tools = AgentToolPolicy.AllowedFor(AgentRole.Lead);

        Assert.Contains(AgentToolKind.ReadFile, tools);
        Assert.Contains(AgentToolKind.InspectGit, tools);
        Assert.Contains(AgentToolKind.SemanticContext, tools);
        Assert.Contains(AgentToolKind.GetSymbolInfo, tools);
        Assert.Contains(AgentToolKind.FindDefinition, tools);
        Assert.Contains(AgentToolKind.RequestVisualCapture, tools);
        Assert.Contains(AgentToolKind.InspectVisualCapture, tools);
        Assert.Contains(AgentToolKind.LookupDocumentation, tools);
        Assert.Contains(AgentToolKind.InspectDependencies, tools);
        Assert.Contains(AgentToolKind.ValidatePackageCandidate, tools);
        Assert.Contains(AgentToolKind.PreviewSbom, tools);
        Assert.Contains(AgentToolKind.PreviewPackageChange, tools);
        Assert.DoesNotContain(AgentToolKind.ApplyFileEdit, tools);
        Assert.DoesNotContain(AgentToolKind.Build, tools);
        Assert.DoesNotContain(AgentToolKind.ListEvidence, tools);
    }

    [Fact]
    public void Implementer_has_approved_mutation_and_verification_without_review_authority()
    {
        IReadOnlyList<AgentToolKind> tools = AgentToolPolicy.AllowedFor(AgentRole.Implementer);

        Assert.Contains(AgentToolKind.ApplyFileEdit, tools);
        Assert.Contains(AgentToolKind.PreviewRename, tools);
        Assert.Contains(AgentToolKind.ApplyRename, tools);
        Assert.Contains(AgentToolKind.PreviewDocumentTransformation, tools);
        Assert.Contains(AgentToolKind.ApplyDocumentTransformation, tools);
        Assert.Contains(AgentToolKind.Build, tools);
        Assert.Contains(AgentToolKind.Test, tools);
        Assert.Contains(AgentToolKind.SemanticContext, tools);
        Assert.Contains(AgentToolKind.InspectCodeProblems, tools);
        Assert.Contains(AgentToolKind.FindReferences, tools);
        Assert.Contains(AgentToolKind.FindImplementations, tools);
        Assert.Contains(AgentToolKind.RequestVisualCapture, tools);
        Assert.Contains(AgentToolKind.InspectVisualCapture, tools);
        Assert.Contains(AgentToolKind.LookupDocumentation, tools);
        Assert.Contains(AgentToolKind.InspectDependencies, tools);
        Assert.DoesNotContain(AgentToolKind.ListEvidence, tools);
    }

    [Fact]
    public void Reviewer_is_independently_read_only_and_can_inspect_evidence()
    {
        IReadOnlyList<AgentToolKind> tools = AgentToolPolicy.AllowedFor(AgentRole.Reviewer);

        Assert.Contains(AgentToolKind.InspectGit, tools);
        Assert.Contains(AgentToolKind.ListEvidence, tools);
        Assert.Contains(AgentToolKind.SemanticContext, tools);
        Assert.Contains(AgentToolKind.GetSymbolInfo, tools);
        Assert.Contains(AgentToolKind.FindReferences, tools);
        Assert.Contains(AgentToolKind.RequestVisualCapture, tools);
        Assert.Contains(AgentToolKind.InspectVisualCapture, tools);
        Assert.Contains(AgentToolKind.LookupDocumentation, tools);
        Assert.Contains(AgentToolKind.PreviewSbom, tools);
        Assert.DoesNotContain(AgentToolKind.ApplyFileEdit, tools);
        Assert.DoesNotContain(AgentToolKind.Build, tools);
        Assert.DoesNotContain(AgentToolKind.Test, tools);
    }

    [Theory]
    [InlineData(AgentRole.Lead,
        "read_file,read_file_range,list_workspace_tree,search_text,search_regex,inspect_git,inspect_dotnet,inspect_project_graph,search_semantic_context,inspect_code_problems,inspect_project_problems,get_symbol_info,find_symbol_definition,find_symbol_references,find_symbol_implementations,search_symbols,analyze_calls,get_type_hierarchy,find_associated_tests,discover_toolsets,request_toolset")]
    [InlineData(AgentRole.Implementer,
        "read_file,read_file_range,list_workspace_tree,search_text,search_regex,inspect_git,inspect_dotnet,inspect_project_graph,search_semantic_context,inspect_code_problems,inspect_project_problems,get_symbol_info,find_symbol_definition,find_symbol_references,find_symbol_implementations,search_symbols,analyze_calls,get_type_hierarchy,find_associated_tests,apply_file_edit,preview_symbol_rename,apply_symbol_rename,preview_document_transformation,apply_document_transformation,dotnet_build,dotnet_test,discover_toolsets,request_toolset")]
    [InlineData(AgentRole.Reviewer,
        "read_file,read_file_range,list_workspace_tree,search_text,search_regex,inspect_git,inspect_dotnet,inspect_project_graph,search_semantic_context,inspect_code_problems,inspect_project_problems,get_symbol_info,find_symbol_definition,find_symbol_references,find_symbol_implementations,search_symbols,analyze_calls,get_type_hierarchy,find_associated_tests,list_tool_evidence,discover_toolsets,request_toolset")]
    public void Factory_exposes_only_the_closed_role_scope(
        AgentRole role,
        string expectedNames)
    {
        AgentToolFactory factory = new(
            new UnsupportedInspectionService(),
            new UnsupportedMutationService(),
            new UnsupportedEvidenceService(),
            new UnsupportedContextService(),
            new UnsupportedCodeIntelligenceService());

        IList<AITool> tools = factory.Create(
            role,
            new("goal-1"),
            role is AgentRole.Implementer ? [new("src")] : []);

        Assert.Equal(expectedNames.Split(','), tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task Factory_namespaces_discovered_mcp_schema_and_invokes_the_exact_tool()
    {
        using System.Text.Json.JsonDocument schema = System.Text.Json.JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"]}");
        CapturingMcpToolService mcp = new(new(
            new("docs-api"),
            new("query/docs"),
            "Documentation lookup",
            "Find exact documentation.",
            schema.RootElement.Clone(),
            null,
            IsReadOnly: true,
            IsDestructive: false,
            IsOpenWorld: false,
            IsAgentEligible: true,
            RejectionReason: null));
        AgentToolFactory factory = new(
            new UnsupportedInspectionService(),
            new UnsupportedMutationService(),
            new UnsupportedEvidenceService(),
            new UnsupportedContextService(),
            new UnsupportedCodeIntelligenceService(),
            mcp);

        AIFunction tool = Assert.IsType<McpAgentFunction>(factory.Create(
            AgentRole.Lead, new("goal-1"), []).Last());
        object? result = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "Avalonia binding",
        });

        Assert.StartsWith("mcp_docs_api_query_docs_", tool.Name, StringComparison.Ordinal);
        Assert.Equal(32, tool.Name.Length);
        Assert.Equal("query/docs", mcp.Invocation?.Tool.Value);
        Assert.Equal("Avalonia binding", mcp.Invocation?.Arguments["query"]);
        Assert.Equal("ok", Assert.IsType<System.Text.Json.JsonElement>(result)
            .GetProperty("result").GetString());
    }

    [Fact]
    public async Task Semantic_tool_uses_the_role_source_context_without_model_snapshot_inputs()
    {
        CapturingCodeIntelligenceService code = new();
        AgentToolFactory factory = new(
            new UnsupportedInspectionService(),
            new UnsupportedMutationService(),
            new UnsupportedEvidenceService(),
            new UnsupportedContextService(),
            code);
        AIFunction tool = Assert.IsAssignableFrom<AIFunction>(factory.Create(
            AgentRole.Reviewer, new("goal-1"), []).Single(item =>
                item.Name == "get_symbol_info"));

        await tool.InvokeAsync(new AIFunctionArguments
        {
            ["relativePath"] = "src/Program.cs",
            ["line"] = 4,
            ["character"] = 7,
        });

        Assert.Equal(GoalWorkspaceScope.ApprovedWorktree, code.Scope);
        Assert.Equal("src/Program.cs", code.Path?.Value);
        Assert.Equal(new WorkbenchCodePosition(4, 7), code.Position);
    }

    [Fact]
    public void Factory_exposes_named_research_tools_without_export_or_generic_network_operations()
    {
        AgentToolFactory factory = new(
            new UnsupportedInspectionService(),
            new UnsupportedMutationService(),
            new UnsupportedEvidenceService(),
            new UnsupportedContextService(),
            new UnsupportedCodeIntelligenceService(),
            documentationResearchService: new UnsupportedDocumentationResearchService(),
            dependencyResearchService: new UnsupportedDependencyResearchService());

        string[] names = factory.Create(AgentRole.Lead, new("goal-1"), [])
            .Select(tool => tool.Name).ToArray();

        Assert.Contains("lookup_documentation", names);
        Assert.Contains("inspect_dependencies", names);
        Assert.Contains("validate_package_candidate", names);
        Assert.Contains("preview_sbom", names);
        Assert.Contains("preview_package_change", names);
        Assert.DoesNotContain("export_sbom", names);
        Assert.DoesNotContain("web_request", names);
        Assert.DoesNotContain("invoke_mcp", names);
    }

    [Theory]
    [InlineData("src/Feature/File.cs", true)]
    [InlineData("src/Feature", true)]
    [InlineData("src/Other/File.cs", false)]
    [InlineData("../outside.cs", false)]
    [InlineData("/absolute.cs", false)]
    public void Delegated_file_areas_confine_implementer_edits(
        string relativePath,
        bool expected)
    {
        bool allowed = AgentToolFactory.IsWithinFileAreas(
            relativePath, [new("src/Feature")]);

        Assert.Equal(expected, allowed);
    }

    private sealed class UnsupportedInspectionService : IGoalWorkspaceInspectionService
    {
        public ValueTask<WorkspaceFileView> ReadFileAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            string relativePath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkspaceTextSearchView> SearchTextAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            string query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkspaceGitStateView> InspectGitAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkspaceDotNetInfoView> InspectDotNetAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedMutationService : IWorkspaceMutationService
    {
        public ValueTask<FileEditView> ApplyFileEditAsync(
            FileEditRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<DotNetOperationView> RunDotNetAsync(
            DotNetOperationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedEvidenceService : IToolEvidenceService
    {
        public ValueTask<ToolEvidenceSnapshot> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedContextService : IGoalContextService
    {
        public ValueTask<SemanticSearchResult> SearchAsync(
            GoalContextRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedCodeIntelligenceService : IGoalCodeIntelligenceService
    {
        public ValueTask<GoalCodeProblemsView> InspectProblemsAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<GoalCodeSymbolView> GetSymbolAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path,
            WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<GoalCodeNavigationView> FindDefinitionAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path,
            WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<GoalCodeNavigationView> FindReferencesAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path,
            WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<GoalCodeNavigationView> FindImplementationsAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path,
            WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedDocumentationResearchService : IDocumentationResearchService
    {
        public ValueTask<DocumentationLookupResult> LookupAsync(
            DocumentationLookupRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedDependencyResearchService : IDependencyResearchService
    {
        public ValueTask<DependencyInspectionResult> InspectAsync(DependencyInspectionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PackageCandidateValidationResult> ValidateCandidateAsync(
            PackageCandidateValidationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SbomPreviewResult> PreviewSbomAsync(SbomPreviewRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PackageChangePreviewResult> PreviewPackageChangeAsync(
            PackageChangePreviewRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SbomExportResult> ExportSbomAsync(SbomExportRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingCodeIntelligenceService : IGoalCodeIntelligenceService
    {
        internal GoalWorkspaceScope? Scope { get; private set; }
        internal WorkbenchCodeDocumentPath? Path { get; private set; }
        internal WorkbenchCodePosition? Position { get; private set; }

        public ValueTask<GoalCodeSymbolView> GetSymbolAsync(
            GoalId goalId,
            GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path,
            WorkbenchCodePosition position,
            CancellationToken cancellationToken = default)
        {
            Scope = scope;
            Path = path;
            Position = position;
            return ValueTask.FromResult(new GoalCodeSymbolView(
                path, position, WorkbenchCodeResultState.Ready, null, [], null));
        }

        public ValueTask<GoalCodeProblemsView> InspectProblemsAsync(
            GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalCodeNavigationView> FindDefinitionAsync(
            GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
            WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalCodeNavigationView> FindReferencesAsync(
            GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
            WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalCodeNavigationView> FindImplementationsAsync(
            GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
            WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingMcpToolService(McpToolDefinition tool) : IMcpToolService
    {
        public IReadOnlyList<McpToolDefinition> EligibleToolsFor(AgentRole role) => [tool];

        internal McpToolInvocation? Invocation { get; private set; }

        public ValueTask<McpToolInvocationResult> InvokeAsync(
            McpToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Invocation = invocation;
            return ValueTask.FromResult(new McpToolInvocationResult(
                "{\"result\":\"ok\"}", false, null, null));
        }
    }
}
