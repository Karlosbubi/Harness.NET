using System.Reflection;
using Harness.BusinessLogic.Goals;

namespace Harness.Architecture.Tests;

public sealed class BusinessLogicServiceDependencyTests
{
    private static readonly string[] ExpectedCrossFeatureDependencies =
    [
        "Harness.BusinessLogic.Agents.AgentRoleRunner -> Harness.BusinessLogic.Inspection.IGoalWorkspaceInspectionService",
        "Harness.BusinessLogic.Agents.AgentRoleRunner -> Harness.BusinessLogic.Mutations.IWorkspaceMutationService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.CodeIntelligence.IChangedSetQualityService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.CodeIntelligence.IGoalCodeIntelligenceService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.Evidence.IToolEvidenceService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.Inspection.IGoalWorkspaceInspectionService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.Mcp.IMcpToolService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.Mutations.IWorkspaceMutationService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.Research.IDependencyResearchService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.Research.IDocumentationResearchService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.Retrieval.IGoalContextService",
        "Harness.BusinessLogic.Agents.AgentToolFactory -> Harness.BusinessLogic.VisualCapture.IVisualCaptureService",
        "Harness.BusinessLogic.Agents.McpAgentFunction -> Harness.BusinessLogic.Mcp.IMcpToolService",
        "Harness.BusinessLogic.CodeIntelligence.ChangedSetQualityService -> Harness.BusinessLogic.Evidence.IToolEvidenceService",
        "Harness.BusinessLogic.CodeIntelligence.ChangedSetQualityService -> Harness.BusinessLogic.Inspection.IGoalWorkspaceInspectionService",
        "Harness.BusinessLogic.CodeIntelligence.GoalCodeIntelligenceService -> Harness.BusinessLogic.Inspection.IGoalWorkspaceInspectionService",
        "Harness.BusinessLogic.Dashboard.ConversationDashboardService -> Harness.BusinessLogic.Workspaces.IWorkspaceService",
        "Harness.BusinessLogic.Documents.WorkbenchDocumentService -> Harness.BusinessLogic.Mutations.IWorkspaceMutationService",
        "Harness.BusinessLogic.Inspection.DeveloperGitService -> Harness.BusinessLogic.Workspaces.IWorkspaceService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Acceptance.IGoalAcceptanceService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Agents.IAgentDefaultsService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Agents.IGoalModelService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.CodeIntelligence.IGoalCodeIntelligenceService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Costs.IRemoteCostService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Evidence.IToolEvidenceService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Goals.IGoalService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Inspection.IDeveloperGitService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Mutations.IWorkspaceMutationService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.VisualCapture.IVisualCaptureService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Workflows.IGoalWorkflowService",
        "Harness.BusinessLogic.Mcp.InboundMcpApplicationService -> Harness.BusinessLogic.Workspaces.IWorkspaceService",
        "Harness.BusinessLogic.Mutations.WorkspaceMutationService -> Harness.BusinessLogic.CodeIntelligence.IWorkbenchCodeIntelligenceService",
        "Harness.BusinessLogic.Retrieval.GoalContextService -> Harness.BusinessLogic.Goals.IGoalService",
        "Harness.BusinessLogic.Workflows.GoalWorkflowService -> Harness.BusinessLogic.Evidence.IToolEvidenceService",
        "Harness.BusinessLogic.Workflows.GoalWorkflowService -> Harness.BusinessLogic.Goals.IGoalService",
        "Harness.BusinessLogic.Workflows.GoalWorkflowService -> Harness.BusinessLogic.Mutations.IWorkspaceMutationService",
    ];

    [Fact]
    public void Cross_feature_service_dependencies_match_the_reviewed_inventory()
    {
        Assembly assembly = typeof(IGoalService).Assembly;
        string[] actual = assembly.GetTypes()
            .Where(type => IsBusinessLogicType(type) && !type.IsInterface)
            .SelectMany(consumer => ReferencedTypes(consumer)
                .Where(service => IsServiceContract(service, assembly))
                .Where(service => FeatureName(service) != FeatureName(consumer))
                .Select(service => $"{consumer.FullName} -> {service.FullName}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            ExpectedCrossFeatureDependencies.SequenceEqual(actual, StringComparer.Ordinal),
            "Cross-feature inventory changed:\n" + string.Join("\n", actual));
    }

    private static IEnumerable<Type> ReferencedTypes(Type consumer)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        IEnumerable<Type> signatures = consumer.GetConstructors(flags)
            .SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType))
            .Concat(consumer.GetFields(flags).Select(field => field.FieldType))
            .Concat(consumer.GetProperties(flags).Select(property => property.PropertyType))
            .Concat(consumer.GetEvents(flags).Select(@event => @event.EventHandlerType).OfType<Type>())
            .Concat(consumer.GetMethods(flags).Select(method => method.ReturnType))
            .Concat(consumer.GetMethods(flags)
                .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)));

        return signatures.SelectMany(FlattenType).Distinct();
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (Type nested in FlattenType(elementType))
                yield return nested;
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type nested in FlattenType(argument))
                yield return nested;
        }
    }

    private static bool IsServiceContract(Type type, Assembly assembly) =>
        type.Assembly == assembly &&
        type.IsInterface &&
        type.Name.StartsWith('I') &&
        type.Name.EndsWith("Service", StringComparison.Ordinal);

    private static bool IsBusinessLogicType(Type type) =>
        type.Namespace?.StartsWith("Harness.BusinessLogic.", StringComparison.Ordinal) is true;

    private static string FeatureName(Type type) =>
        type.Namespace?["Harness.BusinessLogic.".Length..].Split('.')[0]
        ?? throw new InvalidOperationException($"Type {type.FullName} is outside Business Logic.");
}
