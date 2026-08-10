using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Mcp;
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
        Assert.Contains(AgentToolKind.Build, tools);
        Assert.Contains(AgentToolKind.Test, tools);
        Assert.Contains(AgentToolKind.SemanticContext, tools);
        Assert.DoesNotContain(AgentToolKind.ListEvidence, tools);
    }

    [Fact]
    public void Reviewer_is_independently_read_only_and_can_inspect_evidence()
    {
        IReadOnlyList<AgentToolKind> tools = AgentToolPolicy.AllowedFor(AgentRole.Reviewer);

        Assert.Contains(AgentToolKind.InspectGit, tools);
        Assert.Contains(AgentToolKind.ListEvidence, tools);
        Assert.Contains(AgentToolKind.SemanticContext, tools);
        Assert.DoesNotContain(AgentToolKind.ApplyFileEdit, tools);
        Assert.DoesNotContain(AgentToolKind.Build, tools);
        Assert.DoesNotContain(AgentToolKind.Test, tools);
    }

    [Theory]
    [InlineData(AgentRole.Lead,
        "read_file,search_text,inspect_git,inspect_dotnet,search_semantic_context")]
    [InlineData(AgentRole.Implementer,
        "read_file,search_text,inspect_git,inspect_dotnet,search_semantic_context,apply_file_edit,preview_symbol_rename,apply_symbol_rename,dotnet_build,dotnet_test")]
    [InlineData(AgentRole.Reviewer,
        "read_file,search_text,inspect_git,inspect_dotnet,search_semantic_context,list_tool_evidence")]
    public void Factory_exposes_only_the_closed_role_scope(
        AgentRole role,
        string expectedNames)
    {
        AgentToolFactory factory = new(
            new UnsupportedInspectionService(),
            new UnsupportedMutationService(),
            new UnsupportedEvidenceService(),
            new UnsupportedContextService());

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

    private sealed class CapturingMcpToolService(McpToolDefinition tool) : IMcpToolService
    {
        public IReadOnlyList<McpToolDefinition> EligibleTools { get; } = [tool];

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
