using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Models;
using Harness.DataAccess.Models.Ollama;
using Harness.DataAccess.Models.OpenRouter;
using Harness.DataAccess.Agents;
using Harness.DataAccess.Goals;
using Harness.DataAccess.SemanticIndex;
using Harness.DataAccess.Secrets;
using Harness.DataAccess.Workflows;
using Harness.DataAccess.Workspaces;
using Harness.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Harness.Host;

internal static class GoalServiceRegistration
{
    internal static IServiceCollection AddHarnessGoals(
        this IServiceCollection services,
        HarnessConfiguration configuration)
    {
        services.AddSingleton<IGoalAcceptanceService, GoalAcceptanceService>();
        foreach (ModelProviderConfiguration provider in configuration.Providers.Values)
        {
            services.AddKeyedSingleton<IModelProvider>(
                provider.Name,
                (serviceProvider, _) => CreateModelProvider(provider, serviceProvider));
        }

        string mainProviderName = configuration.Providers[configuration.Routing.MainLlm].Name;
        services.AddSingleton<IModelProvider>(provider =>
            provider.GetRequiredKeyedService<IModelProvider>(mainProviderName));

        ModelProviderConfiguration mainProvider = configuration.Providers[mainProviderName];
        services.AddSingleton<GoalModelService>(provider =>
        {
            GoalModelProviderRegistration[] providers = configuration.Providers.Values
                .Select(modelProvider => new GoalModelProviderRegistration(
                    new(modelProvider.Name),
                    modelProvider.Kind is ModelProviderKind.OpenRouter
                        ? ModelAccess.Remote
                        : ModelAccess.Local,
                    new(modelProvider.ChatModel),
                    provider.GetRequiredKeyedService<IModelProvider>(modelProvider.Name)))
                .ToArray();
            Dictionary<AgentRole, ModelProviderName> routes = new()
            {
                [AgentRole.Lead] = new(configuration.Routing.MainLlm),
                [AgentRole.Implementer] = new(configuration.Routing.ToolLlm),
                [AgentRole.Reviewer] = new(configuration.Routing.Reviewer),
            };
            return new(
                provider.GetRequiredService<IGoalStore>(),
                provider.GetRequiredService<IWorkspaceStore>(),
                provider.GetRequiredService<IGoalModelSelectionStore>(),
                provider.GetRequiredService<IAgentRoleDefaultStore>(),
                providers,
                routes,
                provider.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<IGoalModelService>(provider =>
            provider.GetRequiredService<GoalModelService>());
        services.AddSingleton<IGoalModelRouteResolver>(provider =>
            provider.GetRequiredService<GoalModelService>());
        services.AddSingleton<IAgentDefaultsService>(provider =>
            provider.GetRequiredService<GoalModelService>());
        services.AddSingleton<IAgentRoleRunner>(provider => new AgentRoleRunner(
            provider.GetRequiredService<IGoalModelRouteResolver>(),
            new AgentToolFactory(
                provider.GetRequiredService<IGoalWorkspaceInspectionService>(),
                provider.GetRequiredService<IWorkspaceMutationService>(),
                provider.GetRequiredService<IToolEvidenceService>(),
                provider.GetRequiredService<IGoalContextService>(),
                provider.GetRequiredService<IGoalCodeIntelligenceService>(),
                provider.GetRequiredService<IMcpToolService>(),
                provider.GetRequiredService<IVisualCaptureService>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IDocumentationResearchService>(),
                provider.GetRequiredService<IDependencyResearchService>(),
                provider.GetRequiredService<IAgentToolActivationService>(),
                provider.GetRequiredService<IChangedSetQualityService>(),
                provider.GetRequiredService<IInboundMcpUiBridge>()),
            provider.GetRequiredService<ILoggerFactory>(),
            provider.GetRequiredService<IGoalWorkspaceInspectionService>(),
            provider.GetRequiredService<IWorkspaceMutationService>()));
        services.AddSingleton<IGoalWorkflowService>(provider => new GoalWorkflowService(
            provider.GetRequiredService<IGoalWorkflowStore>(),
            provider.GetRequiredService<IGoalWorkflowTaskStore>(),
            provider.GetRequiredService<IGoalService>(),
            provider.GetRequiredService<IAgentRoleRunner>(),
            provider.GetRequiredService<IToolEvidenceService>(),
            provider.GetRequiredService<TimeProvider>()));

        ModelProviderConfiguration embeddingProvider =
            configuration.Providers[configuration.Routing.Embedding];
        services.AddSingleton(new SemanticIndexOptions(
            new(embeddingProvider.Name),
            new(embeddingProvider.EmbeddingModel),
            new(embeddingProvider.EmbeddingDimensions),
            new("line-window-v1"),
            embeddingProvider.Kind is ModelProviderKind.OpenRouter
                ? EmbeddingAccess.Remote
                : EmbeddingAccess.Local,
            EmbeddingBatchSize: 16));
        services.AddSingleton<ISemanticIndexService>(provider => new SemanticIndexService(
            provider.GetRequiredService<IWorkspaceStore>(),
            provider.GetRequiredService<ITrackedTextCatalogReader>(),
            provider.GetRequiredService<ISemanticIndexStore>(),
            provider.GetRequiredKeyedService<IModelProvider>(embeddingProvider.Name),
            provider.GetRequiredService<SemanticIndexOptions>()));
        services.AddSingleton<IGoalContextService, GoalContextService>();
        services.AddSingleton(new ConversationOptions(
            configuration.Conversation.Id,
            configuration.Conversation.Title,
            mainProvider.ChatModel,
            configuration.Conversation.WorkspacePath));
        return services;
    }

    private static IModelProvider CreateModelProvider(
        ModelProviderConfiguration provider,
        IServiceProvider services)
    {
        HttpClient httpClient = new(new SocketsHttpHandler
        {
            ConnectTimeout = provider.ConnectTimeout,
        })
        {
            BaseAddress = provider.Endpoint,
            Timeout = provider.RequestTimeout,
        };
        return provider.Kind switch
        {
            ModelProviderKind.Ollama => new OllamaModelProvider(httpClient),
            ModelProviderKind.OpenRouter => new OpenRouterModelProvider(
                httpClient,
                provider.Name,
                services.GetRequiredService<ISecretStore>(),
                provider.ApiKeyReference ?? throw new InvalidOperationException(
                    $"Provider '{provider.Name}' has no API-key reference."),
                services.GetRequiredService<IRemoteCostStore>()),
            _ => throw new InvalidOperationException(
                $"Provider '{provider.Name}' has unsupported kind '{provider.Kind}'."),
        };
    }
}
