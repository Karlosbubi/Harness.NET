using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Agents;
using Harness.DataAccess.Appearance;
using Harness.DataAccess.Approvals;
using Harness.DataAccess.CodeIntelligence;
using Harness.DataAccess.Commits;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Conversations;
using Harness.DataAccess.Editor;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Framework;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Layouts;
using Harness.DataAccess.Mcp;
using Harness.DataAccess.Models;
using Harness.DataAccess.Models.Configuration;
using Harness.DataAccess.Models.Ollama;
using Harness.DataAccess.Models.OpenRouter;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Observability;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.ProjectSecrets;
using Harness.DataAccess.Research;
using Harness.DataAccess.Secrets;
using Harness.DataAccess.SemanticIndex;
using Harness.DataAccess.VisualCapture;
using Harness.DataAccess.Workflows;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using Harness.Host;
using Harness.Host.Configuration;
using Harness.Presentation.Avalonia;
using Harness.Presentation.Terminal;
using Harness.UI.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BusinessThemeBaseVariant = Harness.BusinessLogic.Appearance.ThemeBaseVariant;
using OperationsBackupResult = Harness.BusinessLogic.Operations.ApplicationBackupResult;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string? evaluationRoot = ArgumentValue(args, "--mcp-evaluation-root");
XdgApplicationPaths applicationPaths = evaluationRoot is null
    ? new()
    : new(CreateEvaluationPaths(evaluationRoot));
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
builder.Services.AddSingleton<IApplicationBackup, SqliteApplicationBackup>();
builder.Services.AddSingleton<IApplicationRestore, SqliteApplicationRestore>();
builder.Services.AddSingleton<IApplicationOperationsService, ApplicationOperationsService>();
builder.Services.AddSingleton<IAppearancePreferenceStore, SqliteAppearancePreferenceStore>();
builder.Services.AddSingleton<IEditorIntelligencePreferenceStore,
    SqliteEditorIntelligencePreferenceStore>();
builder.Services.AddSingleton<IKeybindingPreferenceStore,
    SqliteKeybindingPreferenceStore>();
builder.Services.AddSingleton<IRemoteSpendPreferenceStore, SqliteRemoteSpendPreferenceStore>();
builder.Services.AddSingleton<IUserThemeSource, XdgUserThemeSource>();
if (evaluationRoot is null)
    builder.Services.AddSingleton<ISecretStore, SecretServiceSecretStore>();
else
{
    builder.Services.AddSingleton<VolatileSecretStore>();
    builder.Services.AddSingleton<ISecretStore>(services =>
        services.GetRequiredService<VolatileSecretStore>());
}
builder.Services.AddSingleton(new ModelProviderConfigurationOptions(
    configuration.Providers.Values.Select(provider => new StoredModelProviderConfiguration(
        new(provider.Name),
        provider.Kind is ModelProviderKind.Ollama
            ? StoredModelProviderKind.Ollama
            : StoredModelProviderKind.OpenRouter,
        new(provider.Endpoint),
        new(provider.ChatModel),
        new(provider.EmbeddingModel),
        new(provider.EmbeddingDimensions),
        new(provider.ConnectTimeout),
        new(provider.RequestTimeout),
        provider.ApiKeyReference,
        RequiresRestart: false)).ToArray()));
builder.Services.AddSingleton<IModelProviderConfigurationStore, XdgModelProviderConfigurationStore>();
builder.Services.AddSingleton<IModelProviderSettingsService, ModelProviderSettingsService>();
builder.Services.AddSingleton(new McpConnectionConfigurationOptions(
    configuration.McpConnections.Select(connection => new Harness.DataAccess.Mcp.McpConnectionConfiguration(
        new(connection.Name),
        new(connection.Endpoint),
        new(connection.RequestTimeout),
        connection.IsEnabled,
        RequiresRestart: false,
        Access: connection.Access is McpConnectionAccessKind.HarnessControl
            ? Harness.DataAccess.Mcp.McpConnectionAccess.HarnessControl
            : Harness.DataAccess.Mcp.McpConnectionAccess.ReadOnly,
        ClientId: connection.ClientId is null ? null : new(connection.ClientId),
        AllowedTools: connection.AllowedTools.Select(tool =>
            new Harness.DataAccess.Mcp.McpToolName(tool)).ToArray())).ToArray()));
builder.Services.AddSingleton<IMcpConnectionConfigurationStore, XdgMcpConnectionConfigurationStore>();
builder.Services.AddSingleton<IMcpToolClient, StatelessHttpMcpToolClient>();
builder.Services.AddSingleton<IMcpSettingsService, McpSettingsService>();
builder.Services.AddSingleton<IMcpToolService, McpToolService>();
builder.Services.AddSingleton<IInboundMcpSettingsStore, XdgInboundMcpSettingsStore>();
builder.Services.AddSingleton<IInboundMcpAuditStore, FileInboundMcpAuditStore>();
builder.Services.AddSingleton<IInboundMcpEvaluationFixture, InboundMcpEvaluationFixture>();
builder.Services.AddSingleton(new InboundMcpApplicationEnvironment(evaluationRoot is not null));
builder.Services.AddSingleton<AvaloniaInboundMcpUiBridge>();
builder.Services.AddSingleton<IInboundMcpUiBridge>(services =>
    services.GetRequiredService<AvaloniaInboundMcpUiBridge>());
builder.Services.AddSingleton<IInboundGoalOperationCoordinator,
    InboundGoalOperationCoordinator>();
builder.Services.AddSingleton<IInboundMcpApplication, InboundMcpApplicationService>();
builder.Services.AddSingleton<InboundMcpServer>();
builder.Services.AddSingleton<IInboundMcpRuntime>(services =>
    services.GetRequiredService<InboundMcpServer>());
builder.Services.AddSingleton<IInboundMcpSettingsService, InboundMcpSettingsService>();
builder.Services.AddSingleton(new HttpClient
{
    Timeout = TimeSpan.FromSeconds(60),
    DefaultRequestHeaders =
    {
        { "User-Agent", "Harness.NET/1.0 documentation-dependency-research" },
    },
});
builder.Services.AddSingleton<IResearchSettingsStore, XdgResearchSettingsStore>();
builder.Services.AddSingleton<IDocumentationCache, FileDocumentationCache>();
builder.Services.AddSingleton<IDocumentationSource, LocalPackageDocumentationSource>();
builder.Services.AddSingleton<IDocumentationSource, LocalDocumentationIndexSource>();
builder.Services.AddSingleton<IDocumentationSource, McpDocumentationSource>();
builder.Services.AddSingleton<IDocumentationSource, HttpDocumentationSource>();
builder.Services.AddSingleton<IDependencyEvidenceReader, DependencyEvidenceReader>();
builder.Services.AddSingleton<IPackageCandidateMetadataClient, NuGetPackageCandidateMetadataClient>();
builder.Services.AddSingleton<ISbomExporter, AtomicSbomExporter>();
builder.Services.AddSingleton<ResearchWorkspaceResolver>();
builder.Services.AddSingleton<IDocumentationResearchService, DocumentationResearchService>();
builder.Services.AddSingleton<IDependencyResearchService, DependencyResearchService>();
builder.Services.AddSingleton<IResearchSettingsService, ResearchSettingsService>();
builder.Services.AddSingleton<IConversationStore, SqliteConversationStore>();
builder.Services.AddSingleton<IFrameworkSourceReader, FileFrameworkSourceReader>();
builder.Services.AddSingleton<IFrameworkOverlayStore, SqliteFrameworkOverlayStore>();
builder.Services.AddSingleton<IWorkspaceInspector, GitWorkspaceInspector>();
builder.Services.AddSingleton<IWorkspaceStore, SqliteWorkspaceStore>();
builder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();
builder.Services.AddSingleton<IGoalStore, SqliteGoalStore>();
builder.Services.AddSingleton<IGoalModelSelectionStore, SqliteGoalModelSelectionStore>();
builder.Services.AddSingleton<IAgentRoleDefaultStore, SqliteAgentRoleDefaultStore>();
builder.Services.AddSingleton<IAgentToolExposureConfigurationStore, XdgAgentToolExposureConfigurationStore>();
builder.Services.AddSingleton<IAgentToolExposureSettingsService, AgentToolExposureSettingsService>();
builder.Services.AddSingleton<IRemoteCostStore, SqliteRemoteCostStore>();
builder.Services.AddSingleton<IRemoteCostService, RemoteCostService>();
builder.Services.AddSingleton<ICapabilityApprovalStore, SqliteCapabilityApprovalStore>();
builder.Services.AddSingleton<ICapabilityApprovalService, CapabilityApprovalService>();
builder.Services.AddSingleton<IToolEvidenceStore, SqliteToolEvidenceStore>();
builder.Services.AddSingleton<IToolEvidenceService, ToolEvidenceService>();
builder.Services.AddSingleton<IAgentToolActivationService, AgentToolActivationService>();
builder.Services.AddSingleton<ISensitiveDisplayGuard, SensitiveDisplayGuard>();
builder.Services.AddSingleton<IVisualCapturePreferenceStore, SqliteVisualCapturePreferenceStore>();
builder.Services.AddSingleton<IVisualCapturePortal, XdgDesktopPortalVisualCapture>();
builder.Services.AddSingleton<IVisualCaptureImageSourceReader, PortalFileImageSourceReader>();
builder.Services.AddSingleton<IVisualCaptureArtifactStore, FileVisualCaptureArtifactStore>();
builder.Services.AddSingleton<IVisualCaptureService, VisualCaptureService>();
builder.Services.AddSingleton<IRunOutputService, RunOutputService>();
builder.Services.AddSingleton<IDotNetProjectRunner, DotNetProjectRunner>();
builder.Services.AddSingleton<IDeveloperDotNetExecutionStore,
    SqliteDeveloperDotNetExecutionStore>();
builder.Services.AddSingleton<IDeveloperProjectExecutionService,
    DeveloperProjectExecutionService>();
builder.Services.AddSingleton<IGoalService, GoalService>();
builder.Services.AddSingleton<IGoalWorktreeManager, GitGoalWorktreeManager>();
builder.Services.AddSingleton<IWorkspaceFileEditor, AtomicWorkspaceFileEditor>();
builder.Services.AddSingleton<IDotNetToolRunner, DotNetToolRunner>();
builder.Services.AddSingleton<IWorkspaceMutationService, WorkspaceMutationService>();
builder.Services.AddSingleton<IWorkspaceFileReader, WorkspaceFileReader>();
builder.Services.AddSingleton<IWorkspaceFileCatalogReader, GitWorkspaceFileCatalogReader>();
builder.Services.AddSingleton<IWorkspaceTextSearcher, GitWorkspaceTextSearcher>();
builder.Services.AddSingleton<IWorkspaceAdvancedInspector, GitWorkspaceAdvancedInspector>();
builder.Services.AddSingleton<IWorkspaceGitInspector, LibGitWorkspaceGitInspector>();
builder.Services.AddSingleton<IWorkspaceDotNetInspector, WorkspaceDotNetInspector>();
builder.Services.AddSingleton<IProjectUserSecretsPathResolver,
    PlatformProjectUserSecretsPathResolver>();
builder.Services.AddSingleton<IProjectUserSecretStore, ProjectUserSecretStore>();
builder.Services.AddSingleton<IProjectUserSecretsService, ProjectUserSecretsService>();
builder.Services.AddSingleton<IDotNetProcess, DotNetProcess>();
builder.Services.AddSingleton<DotNetSdkSelector>();
builder.Services.AddSingleton<IMSBuildRuntime, MSBuildRuntime>();
builder.Services.AddSingleton<IRoslynWorkspaceProbe, RoslynWorkspaceProbe>();
builder.Services.AddSingleton<ICodeIntelligenceEngine, RoslynCodeIntelligenceEngine>();
builder.Services.AddSingleton<IWorkspaceInspectionService, WorkspaceInspectionService>();
builder.Services.AddSingleton<IWorkbenchWorkspaceContextResolver, WorkbenchWorkspaceContextResolver>();
builder.Services.AddSingleton<IWorkbenchCodeIntelligenceService, WorkbenchCodeIntelligenceService>();
builder.Services.AddSingleton<IKeybindingSettingsService, KeybindingSettingsService>();
builder.Services.AddSingleton<IWorkbenchInspectionService, WorkbenchInspectionService>();
builder.Services.AddSingleton<IWorkbenchDocumentService, WorkbenchDocumentService>();
builder.Services.AddSingleton<IWorkbenchLayoutStore, FileWorkbenchLayoutStore>();
builder.Services.AddSingleton<IWorkbenchLayoutService, WorkbenchLayoutService>();
builder.Services.AddSingleton<IGoalWorkspaceInspectionService, GoalWorkspaceInspectionService>();
builder.Services.AddSingleton<IGoalCodeIntelligenceService, GoalCodeIntelligenceService>();
builder.Services.AddSingleton<IChangedSetQualityService, ChangedSetQualityService>();
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
builder.Services.AddSingleton<IGoalWorkflowStore, SqliteGoalWorkflowStore>();
builder.Services.AddSingleton<IGoalWorkflowTaskStore, SqliteGoalWorkflowTaskStore>();
builder.Services.AddSingleton<IGoalCommitApprovalStore, SqliteGoalCommitApprovalStore>();
builder.Services.AddSingleton<IGoalCommitter, LibGitGoalCommitter>();
builder.Services.AddSingleton(TimeProvider.System);
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
        services.GetRequiredService<IAgentRoleDefaultStore>(),
        providers,
        routes,
        services.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<IGoalModelService>(services =>
    services.GetRequiredService<GoalModelService>());
builder.Services.AddSingleton<IGoalModelRouteResolver>(services =>
    services.GetRequiredService<GoalModelService>());
builder.Services.AddSingleton<IAgentDefaultsService>(services =>
    services.GetRequiredService<GoalModelService>());
builder.Services.AddSingleton<IAgentRoleRunner>(services => new AgentRoleRunner(
    services.GetRequiredService<IGoalModelRouteResolver>(),
    new AgentToolFactory(
        services.GetRequiredService<IGoalWorkspaceInspectionService>(),
        services.GetRequiredService<IWorkspaceMutationService>(),
        services.GetRequiredService<IToolEvidenceService>(),
        services.GetRequiredService<IGoalContextService>(),
        services.GetRequiredService<IGoalCodeIntelligenceService>(),
        services.GetRequiredService<IMcpToolService>(),
        services.GetRequiredService<IVisualCaptureService>(),
        services.GetRequiredService<TimeProvider>(),
        services.GetRequiredService<IDocumentationResearchService>(),
        services.GetRequiredService<IDependencyResearchService>(),
        services.GetRequiredService<IAgentToolActivationService>(),
        services.GetRequiredService<IChangedSetQualityService>(),
        services.GetRequiredService<IInboundMcpUiBridge>()),
    services.GetRequiredService<ILoggerFactory>(),
    services.GetRequiredService<IGoalWorkspaceInspectionService>(),
    services.GetRequiredService<IWorkspaceMutationService>()));
builder.Services.AddSingleton<IGoalWorkflowService>(services => new GoalWorkflowService(
    services.GetRequiredService<IGoalWorkflowStore>(),
    services.GetRequiredService<IGoalWorkflowTaskStore>(),
    services.GetRequiredService<IGoalService>(),
    services.GetRequiredService<IAgentRoleRunner>(),
    services.GetRequiredService<IToolEvidenceService>(),
    services.GetRequiredService<TimeProvider>()));
ModelProviderConfiguration embeddingProvider =
    configuration.Providers[configuration.Routing.Embedding];
builder.Services.AddSingleton(new SemanticIndexOptions(
    new(embeddingProvider.Name),
    new(embeddingProvider.EmbeddingModel),
    new(embeddingProvider.EmbeddingDimensions),
    new("line-window-v1"),
    embeddingProvider.Kind is ModelProviderKind.OpenRouter
        ? EmbeddingAccess.Remote
        : EmbeddingAccess.Local,
    EmbeddingBatchSize: 16));
builder.Services.AddSingleton<ISemanticIndexService>(services => new SemanticIndexService(
    services.GetRequiredService<IWorkspaceStore>(),
    services.GetRequiredService<ITrackedTextCatalogReader>(),
    services.GetRequiredService<ISemanticIndexStore>(),
    services.GetRequiredKeyedService<IModelProvider>(embeddingProvider.Name),
    services.GetRequiredService<SemanticIndexOptions>()));
builder.Services.AddSingleton<IGoalContextService, GoalContextService>();
builder.Services.AddSingleton(new ConversationOptions(
    configuration.Conversation.Id,
    configuration.Conversation.Title,
    mainProvider.ChatModel,
    configuration.Conversation.WorkspacePath));
builder.Services.AddSingleton<IDashboardService, ConversationDashboardService>();
builder.Services.AddSingleton(new AppearanceOptions(HarnessThemeCatalog.BuiltIns
    .Select(theme => new BuiltInThemeRegistration(
        new(theme.Id.Value),
        theme.DisplayName,
        theme.BaseVariant switch
        {
            UiThemeBaseVariant.System => BusinessThemeBaseVariant.System,
            UiThemeBaseVariant.Light => BusinessThemeBaseVariant.Light,
            UiThemeBaseVariant.Dark => BusinessThemeBaseVariant.Dark,
            UiThemeBaseVariant.HighContrast => BusinessThemeBaseVariant.HighContrast,
            _ => throw new InvalidOperationException("Unsupported UI theme variant."),
        }))
    .ToArray()));
builder.Services.AddSingleton<IAppearanceService, AppearanceService>();
builder.Services.AddSingleton<IEditorIntelligenceSettingsService,
    EditorIntelligenceSettingsService>();
builder.Services.AddSingleton<IRemoteSpendPreferenceService, RemoteSpendPreferenceService>();
builder.Services.AddSingleton<AvaloniaPresentationStore>();
builder.Services.AddSingleton<HarnessThemeController>();
builder.Services.AddSingleton<IAvaloniaShell, AvaloniaShell>();
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
    ApplicationRestoreApplyResult restore = await host.Services
        .GetRequiredService<IApplicationRestore>()
        .ApplyPendingAsync(shutdown.Token);
    if (restore.HadPendingRestore && !restore.Applied)
    {
        throw new InvalidOperationException(
            $"Pending application restore failed safely: {restore.Error}");
    }

    await host.StartAsync(shutdown.Token);
    DatabaseInitializationResult database = await host.Services
        .GetRequiredService<IDatabaseInitializer>()
        .InitializeAsync(shutdown.Token);
    if (evaluationRoot is not null)
    {
        InboundMcpEvaluationSnapshot fixture = await host.Services
            .GetRequiredService<IInboundMcpEvaluationFixture>()
            .EnsureAsync(shutdown.Token);
        IWorkspaceService workspaces = host.Services.GetRequiredService<IWorkspaceService>();
        WorkspaceResult registered = await workspaces.RegisterAsync(
            fixture.RootPath, fixture.EntryPoint, shutdown.Token);
        if (registered.Workspace is null)
            throw new InvalidOperationException($"Evaluation fixture registration failed: {registered.Error}");
        await workspaces.SetTrustAsync(registered.Workspace.Id, true, shutdown.Token);
    }
    await host.Services.GetRequiredService<IInboundMcpRuntime>().ApplyAsync(shutdown.Token);
    await host.Services.GetRequiredService<IVisualCaptureService>()
        .CleanupAsync(shutdown.Token);
    await host.Services.GetRequiredService<IMcpSettingsService>()
        .RefreshAsync(shutdown.Token);

    ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    if (restore.Applied)
    {
        logger.LogInformation(
            "Applied verified application restore at schema {SchemaVersion}; previous state is at {RollbackDirectory}",
            restore.RestoredSchemaVersion?.Value,
            restore.RollbackDirectory);
    }
    logger.LogInformation(
        "Harness.NET initialized schema {SchemaVersion} at {DatabasePath}",
        database.SchemaVersion.Value,
        database.DatabasePath.Value);
    if (database.PreUpgradeBackup is not null)
    {
        logger.LogInformation(
            "Created verified pre-upgrade backup at {BackupPath}",
            database.PreUpgradeBackup.Value);
    }

    string? backupPath = HostRunModeResolver.BackupPath(args);
    HostRunMode runMode = HostRunModeResolver.Resolve(
        args, Console.IsInputRedirected, Console.IsOutputRedirected);
    if (backupPath is not null)
    {
        OperationsBackupResult backup = await host.Services
            .GetRequiredService<IApplicationOperationsService>()
            .CreateBackupAsync(new(backupPath), shutdown.Token);
        if (backup.Backup is null)
        {
            throw new InvalidOperationException($"Backup failed: {backup.Error}");
        }

        Console.WriteLine(
            $"Harness.NET backup created (schema {backup.Backup.SchemaVersion.Value}, " +
            $"sha256 {backup.Backup.ArchiveSha256.Value})");
    }
    else if (runMode is HostRunMode.Interactive)
    {
        InteractiveFrontend frontend = HostRunModeResolver.ResolveFrontend(
            args, Console.IsInputRedirected, Console.IsOutputRedirected);
        if (frontend is InteractiveFrontend.Avalonia)
        {
            await host.Services.GetRequiredService<IAvaloniaShell>().RunAsync(shutdown.Token);
        }
        else
        {
            await host.Services.GetRequiredService<ITerminalShell>().RunAsync(shutdown.Token);
        }
    }
    else
    {
        Console.WriteLine($"Harness.NET ready (schema {database.SchemaVersion.Value})");
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

static string? ArgumentValue(string[] arguments, string option)
{
    int index = Array.FindIndex(arguments, value => value.Equals(option, StringComparison.Ordinal));
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

static ApplicationPaths CreateEvaluationPaths(string requestedRoot)
{
    string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requestedRoot));
    string temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
    if (!root.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        root.Equals(temporary, StringComparison.Ordinal))
    {
        throw new ArgumentException("The MCP evaluation root must be a dedicated directory below the system temporary directory.");
    }

    return new(
        Path.Combine(root, "config"),
        Path.Combine(root, "data"),
        Path.Combine(root, "state"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));
}
