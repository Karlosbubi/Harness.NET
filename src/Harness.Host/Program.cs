using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Conversations;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Framework;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Models;
using Harness.DataAccess.Models.Ollama;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Observability;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Secrets;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
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
foreach (ModelProviderConfiguration provider in configuration.Providers.Values)
{
    builder.Services.AddKeyedSingleton<IModelProvider>(
        provider.Name,
        (_, _) => CreateModelProvider(provider));
}

string mainProviderName = configuration.Providers[configuration.Routing.MainLlm].Name;
builder.Services.AddSingleton<IModelProvider>(services =>
    services.GetRequiredKeyedService<IModelProvider>(mainProviderName));

ModelProviderConfiguration mainProvider = configuration.Providers[mainProviderName];
builder.Services.AddSingleton(new ConversationOptions(
    configuration.Conversation.Id,
    configuration.Conversation.Title,
    mainProvider.ChatModel,
    configuration.Conversation.WorkspacePath));
builder.Services.AddSingleton<IDashboardService, ConversationDashboardService>();
builder.Services.AddSingleton<ITerminalShell, TerminalGuiShell>();

using IHost host = builder.Build();
using CancellationTokenSource shutdown = new();
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

    bool noUi = args.Contains("--no-ui", StringComparer.Ordinal);
    if (!noUi && !Console.IsInputRedirected && !Console.IsOutputRedirected)
    {
        await host.Services.GetRequiredService<ITerminalShell>().RunAsync(shutdown.Token);
    }
    else
    {
        Console.WriteLine($"Harness.NET ready (schema {database.SchemaVersion})");
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

static IModelProvider CreateModelProvider(ModelProviderConfiguration provider) =>
    provider.Kind.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
        ? new OllamaModelProvider(new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = provider.ConnectTimeout,
        })
        {
            BaseAddress = provider.Endpoint,
            Timeout = provider.RequestTimeout,
        })
        : throw new InvalidOperationException(
            $"Provider '{provider.Name}' has unsupported kind '{provider.Kind}'.");
