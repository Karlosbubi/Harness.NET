using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Workspaces;
using Harness.BusinessLogic.Workflows;
using Harness.DataAccess.Approvals;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Commits;
using Harness.DataAccess.Conversations;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Framework;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Models;
using Harness.DataAccess.Models.Ollama;
using Harness.DataAccess.Models.OpenRouter;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Observability;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Secrets;
using Harness.DataAccess.SemanticIndex;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using Harness.DataAccess.Workflows;
using Harness.Host;
using Harness.Host.Configuration;
using Harness.Presentation.Terminal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
XdgApplicationPaths applicationPaths = new();
HarnessConfiguration configuration = HarnessConfigurationLoader.Load(
    args,
    applicationPaths.Current,
    AppContext.BaseDirectory);
ObservabilityOptions observabilityOptions = new(
    applicationPaths.Current.LogDirectory,
    configuration.Observability.OtlpEndpoint);

ObservabilityBootstrap.Configure(builder.Services, observabilityOptions);
builder.Services.AddSingleton<IApplicationPaths>(applicationPaths);
builder.Services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
builder.Services.AddSingleton<ISecretStore, SecretServiceSecretStore>();
builder.Services.AddSingleton<IConversationStore, SqliteConversationStore>();
builder.Services.AddSingleton<IFrameworkSourceReader, FileFrameworkSourceReader>();
builder.Services.AddSingleton<IFrameworkOverlayStore, SqliteFrameworkOverlayStore>();
builder.Services.AddSingleton<IWorkspaceInspector, GitWorkspaceInspector>();
builder.Services.AddSingleton<IWorkspaceStore, SqliteWorkspaceStore>();
builder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();
builder.Services.AddSingleton<IGoalStore, SqliteGoalStore>();
builder.Services.AddSingleton<IGoalModelSelectionStore, SqliteGoalModelSelectionStore>();
builder.Services.AddSingleton<IRemoteCostStore, SqliteRemoteCostStore>();
builder.Services.AddSingleton<IRemoteCostService, RemoteCostService>();
builder.Services.AddSingleton<ICapabilityApprovalStore, SqliteCapabilityApprovalStore>();
builder.Services.AddSingleton<ICapabilityApprovalService, CapabilityApprovalService>();
builder.Services.AddSingleton<IToolEvidenceStore, SqliteToolEvidenceStore>();
builder.Services.AddSingleton<IToolEvidenceService, ToolEvidenceService>();
builder.Services.AddSingleton<IGoalService, GoalService>();
builder.Services.AddSingleton<IGoalWorktreeManager, GitGoalWorktreeManager>();
builder.Services.AddSingleton<IWorkspaceFileEditor, AtomicWorkspaceFileEditor>();
builder.Services.AddSingleton<IDotNetToolRunner, DotNetToolRunner>();
builder.Services.AddSingleton<IWorkspaceMutationService, WorkspaceMutationService>();
builder.Services.AddSingleton<IWorkspaceFileReader, WorkspaceFileReader>();
builder.Services.AddSingleton<IWorkspaceTextSearcher, GitWorkspaceTextSearcher>();
builder.Services.AddSingleton<IWorkspaceGitInspector, LibGitWorkspaceGitInspector>();
builder.Services.AddSingleton<IWorkspaceDotNetInspector, WorkspaceDotNetInspector>();
builder.Services.AddSingleton<IWorkspaceInspectionService, WorkspaceInspectionService>();
builder.Services.AddSingleton<IGoalWorkspaceInspectionService, GoalWorkspaceInspectionService>();
builder.Services.AddSingleton<ITrackedTextCatalogReader, GitTrackedTextCatalogReader>();
builder.Services.AddSingleton<ISemanticIndexStore, SqliteSemanticIndexStore>();
builder.Services.AddSingleton<IFrameworkResolver, FrameworkResolver>();
builder.Services.AddSingleton(new FrameworkOptions(configuration.Framework.Rules
    .Select(rule => new FrameworkRule(
        rule.Key,
        rule.Value,
        rule.Precedence,
        rule.Layer,
        rule.IsLocked,
        rule.Source))
    .ToArray()));
builder.Services.AddSingleton<IFrameworkService, FrameworkService>();
builder.Services.AddSingleton<IWorkflowCheckpointStore, SqliteWorkflowCheckpointStore>();
builder.Services.AddSingleton<IGoalWorkflowStore, SqliteGoalWorkflowStore>();
builder.Services.AddSingleton<IGoalWorkflowTaskStore, SqliteGoalWorkflowTaskStore>();
builder.Services.AddSingleton<IGoalCommitApprovalStore, SqliteGoalCommitApprovalStore>();
builder.Services.AddSingleton<IGoalCommitter, LibGitGoalCommitter>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IWalkingSkeletonWorkflowService, WalkingSkeletonWorkflowService>();
builder.Services.AddSingleton<IGoalAcceptanceService, GoalAcceptanceService>();
foreach (ModelProviderConfiguration provider in configuration.Providers.Values)
{
    builder.Services.AddKeyedSingleton<IModelProvider>(
        provider.Name,
        (services, _) => CreateModelProvider(provider, services));
}

string mainProviderName = configuration.Providers[configuration.Routing.MainLlm].Name;
builder.Services.AddSingleton<IModelProvider>(services =>
    services.GetRequiredKeyedService<IModelProvider>(mainProviderName));

ModelProviderConfiguration mainProvider = configuration.Providers[mainProviderName];
builder.Services.AddSingleton<GoalModelService>(services =>
{
    GoalModelProviderRegistration[] providers = configuration.Providers.Values
        .Select(provider => new GoalModelProviderRegistration(
            new(provider.Name),
            provider.Kind is ModelProviderKind.OpenRouter
                ? ModelAccess.Remote
                : ModelAccess.Local,
            new(provider.ChatModel),
            services.GetRequiredKeyedService<IModelProvider>(provider.Name)))
        .ToArray();
    Dictionary<AgentRole, ModelProviderName> routes = new()
    {
        [AgentRole.Lead] = new(configuration.Routing.MainLlm),
        [AgentRole.Implementer] = new(configuration.Routing.ToolLlm),
        [AgentRole.Reviewer] = new(configuration.Routing.Reviewer),
    };
    return new(
        services.GetRequiredService<IGoalStore>(),
        services.GetRequiredService<IWorkspaceStore>(),
        services.GetRequiredService<IGoalModelSelectionStore>(),
        providers,
        routes,
        services.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<IGoalModelService>(services =>
    services.GetRequiredService<GoalModelService>());
builder.Services.AddSingleton<IGoalModelRouteResolver>(services =>
    services.GetRequiredService<GoalModelService>());
builder.Services.AddSingleton<IAgentRoleRunner>(services => new AgentRoleRunner(
    services.GetRequiredService<IGoalModelRouteResolver>(),
    new AgentToolFactory(
        services.GetRequiredService<IGoalWorkspaceInspectionService>(),
        services.GetRequiredService<IWorkspaceMutationService>(),
        services.GetRequiredService<IToolEvidenceService>()),
    services.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<IGoalWorkflowService>(services => new GoalWorkflowService(
    services.GetRequiredService<IGoalWorkflowStore>(),
    services.GetRequiredService<IGoalWorkflowTaskStore>(),
    services.GetRequiredService<IGoalService>(),
    services.GetRequiredService<IAgentRoleRunner>(),
    services.GetRequiredService<TimeProvider>()));
ModelProviderConfiguration embeddingProvider =
    configuration.Providers[configuration.Routing.Embedding];
builder.Services.AddSingleton(new SemanticIndexOptions(
    new(embeddingProvider.Name),
    new(embeddingProvider.EmbeddingModel),
    new(embeddingProvider.EmbeddingDimensions),
    new("line-window-v1"),
    EmbeddingBatchSize: 16));
builder.Services.AddSingleton<ISemanticIndexService>(services => new SemanticIndexService(
    services.GetRequiredService<IWorkspaceStore>(),
    services.GetRequiredService<ITrackedTextCatalogReader>(),
    services.GetRequiredService<ISemanticIndexStore>(),
    services.GetRequiredKeyedService<IModelProvider>(embeddingProvider.Name),
    services.GetRequiredService<SemanticIndexOptions>()));
builder.Services.AddSingleton(new ConversationOptions(
    configuration.Conversation.Id,
    configuration.Conversation.Title,
    mainProvider.ChatModel,
    configuration.Conversation.WorkspacePath));
builder.Services.AddSingleton<IDashboardService, ConversationDashboardService>();
builder.Services.AddSingleton<ITerminalShell, TerminalGuiShell>();

using IHost host = builder.Build();
IHostApplicationLifetime applicationLifetime =
    host.Services.GetRequiredService<IHostApplicationLifetime>();
using CancellationTokenSource shutdown = CancellationTokenSource.CreateLinkedTokenSource(
    applicationLifetime.ApplicationStopping);
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    await host.StartAsync(shutdown.Token);
    DatabaseInitializationResult database = await host.Services
        .GetRequiredService<IDatabaseInitializer>()
        .InitializeAsync(shutdown.Token);

    ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogInformation(
        "Harness.NET initialized schema {SchemaVersion} at {DatabasePath}",
        database.SchemaVersion,
        database.DatabasePath);

    HostRunMode runMode = HostRunModeResolver.Resolve(
        args,
        Console.IsInputRedirected,
        Console.IsOutputRedirected);
    if (runMode is HostRunMode.Interactive)
    {
        await host.Services.GetRequiredService<ITerminalShell>().RunAsync(shutdown.Token);
    }
    else
    {
        Console.WriteLine($"Harness.NET ready (schema {database.SchemaVersion})");
        if (runMode is HostRunMode.WaitForShutdown)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
        }
    }

    await host.StopAsync(CancellationToken.None);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    await host.StopAsync(CancellationToken.None);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static IModelProvider CreateModelProvider(
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
