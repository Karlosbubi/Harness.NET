using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mcp;
using Harness.DataAccess.Observability;
using Harness.DataAccess.Persistence;
using Harness.Host;
using Harness.Host.Configuration;
using Harness.Presentation.Avalonia;
using Harness.Presentation.Terminal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
builder.Services.AddHarnessInfrastructure(applicationPaths, configuration, evaluationRoot);
builder.Services.AddHarnessIntegrations(configuration, evaluationRoot is not null);
builder.Services.AddHarnessWorkspace(configuration);
builder.Services.AddHarnessGoals(configuration);
builder.Services.AddHarnessPresentation();

using IHost host = builder.Build();
IHostApplicationLifetime applicationLifetime =
    host.Services.GetRequiredService<IHostApplicationLifetime>();
using CancellationTokenSource shutdown = CancellationTokenSource.CreateLinkedTokenSource(
    applicationLifetime.ApplicationStopping);
using IDisposable terminationSignal = HostShutdownSignals.RegisterTermination(shutdown);
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
        logger.LogInformation("Interactive frontend stopped");
    }
    else
    {
        Console.WriteLine($"Harness.NET ready (schema {database.SchemaVersion.Value})");
        if (runMode is HostRunMode.WaitForShutdown)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
        }
    }

    logger.LogInformation("Stopping Harness.NET host");
    await host.StopAsync(CancellationToken.None);
    logger.LogInformation("Harness.NET host stopped");
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    await host.StopAsync(CancellationToken.None);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
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
