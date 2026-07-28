using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Retrieval;
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
        "read_file,search_text,inspect_git,inspect_dotnet,search_semantic_context,apply_file_edit,dotnet_build,dotnet_test")]
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
}
