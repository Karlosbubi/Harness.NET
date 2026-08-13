using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Execution;

internal sealed class DeveloperProjectExecutionService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IWorkspaceStore workspaceStore,
    IWorkspaceDotNetInspector dotNetInspector,
    IWorkspaceFileReader fileReader,
    IDotNetProjectRunner runner,
    IDeveloperDotNetExecutionStore store,
    TimeProvider timeProvider,
    ILogger<DeveloperProjectExecutionService> logger) :
    IDeveloperProjectExecutionService, IDisposable
{
    private const int MaximumExecutions = 200;
    private const int MaximumConcurrentExecutions = 4;
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> active = new();
    private readonly ConcurrentDictionary<string, TransientOutput> output = new();
    private readonly SemaphoreSlim reconciliationGate = new(1, 1);
    private readonly SemaphoreSlim executionSlots = new(
        MaximumConcurrentExecutions, MaximumConcurrentExecutions);
    private int reconciled;
    private bool disposed;

    public DeveloperExecutionCapabilities Capabilities { get; } = new(
        CanRunProjectEntryPoint: true,
        CanDebugProjectEntryPoint: false,
        "Debug requires a pinned debugger adapter; ordinary Run is not labeled Debug.");

    public async ValueTask<DeveloperExecutionStartResult> StartRunAsync(
        DeveloperExecutionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        await EnsureReconciledAsync(cancellationToken);
        Resolution resolution = await ResolveAsync(request.Workspace, request.Target,
            cancellationToken);
        if (resolution.RootPath is null || resolution.Context is null)
        {
            return new(null, resolution.ErrorCode, resolution.Error);
        }
        if (!await executionSlots.WaitAsync(0, cancellationToken))
        {
            return new(null, "execution_limit_reached",
                $"At most {MaximumConcurrentExecutions} project runs may be active.");
        }

        DeveloperExecutionId id = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        StoredDeveloperExecution stored;
        try
        {
            stored = await store.StartAsync(new(
                new(id.Value),
                new(request.Workspace.WorkspaceId.Value),
                resolution.Context.GoalId is null ? null : new(resolution.Context.GoalId.Value),
                new(resolution.Context.Description),
                new(request.Target.ProjectPath.Value),
                request.Target.TargetFramework.Value == "unknown"
                    ? null
                    : new(request.Target.TargetFramework.Value),
                new(request.Target.DeclarationId.Value),
                startedAt), cancellationToken);
        }
        catch (Exception exception)
        {
            executionSlots.Release();
            logger.LogWarning(exception,
                "Could not persist developer project execution {ExecutionId}.", id.Value);
            return new(null, "execution_state_unavailable",
                "The project run could not be recorded safely.");
        }
        CancellationTokenSource executionCancellation = new();
        if (!active.TryAdd(id.Value, executionCancellation))
        {
            executionCancellation.Dispose();
            executionSlots.Release();
            return new(null, "execution_identity_conflict",
                "The project run identity already exists.");
        }

        _ = ExecuteAsync(
            stored,
            request.Target,
            resolution.RootPath,
            executionCancellation);
        return new(Map(stored, request.Target), null, null);
    }

    public async ValueTask<DeveloperExecutionListResult> ListAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await EnsureReconciledAsync(cancellationToken);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request, cancellationToken);
        if (resolution.Error is not null)
        {
            return new([], false, resolution.ErrorCode, resolution.Error);
        }
        StoredDeveloperExecution[] executions = (await store.ListAsync(
            new(request.WorkspaceId.Value),
            resolution.Context.GoalId is null ? null : new(resolution.Context.GoalId.Value),
            MaximumExecutions,
            cancellationToken)).ToArray();
        return new(
            executions.Select(item => Map(item, Target(item))).ToArray(),
            executions.Length >= MaximumExecutions,
            null,
            null);
    }

    public ValueTask<DeveloperExecutionCancelResult> CancelAsync(
        DeveloperExecutionId executionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (executionId is null || string.IsNullOrWhiteSpace(executionId.Value))
        {
            return ValueTask.FromResult(new DeveloperExecutionCancelResult(
                false, "invalid_execution", "A project run is required."));
        }
        if (!active.TryGetValue(executionId.Value, out CancellationTokenSource? source))
        {
            return ValueTask.FromResult(new DeveloperExecutionCancelResult(
                false, "execution_not_running", "The selected project run is not active."));
        }
        source.Cancel();
        return ValueTask.FromResult(new DeveloperExecutionCancelResult(true, null, null));
    }

    private async Task ExecuteAsync(
        StoredDeveloperExecution execution,
        WorkbenchExecutionTarget target,
        string rootPath,
        CancellationTokenSource cancellation)
    {
        DotNetProjectExecutionResult result;
        try
        {
            result = await runner.RunAsync(rootPath, new(
                new(target.ProjectPath.Value),
                target.TargetFramework.Value == "unknown"
                    ? null
                    : new(target.TargetFramework.Value)), cancellation.Token);
        }
        catch (Exception exception)
        {
            result = new(
                new(target.ProjectPath.Value),
                target.TargetFramework.Value == "unknown"
                    ? null
                    : new(target.TargetFramework.Value),
                null, new(string.Empty), new(string.Empty), false, false, false, 0,
                "project_run_failed", exception.Message);
        }

        StoredDeveloperExecutionState state = result.WasCancelled
            ? StoredDeveloperExecutionState.Cancelled
            : result.ErrorCode is not null || result.ExitCode != 0
                ? StoredDeveloperExecutionState.Failed
                : StoredDeveloperExecutionState.Succeeded;
        try
        {
            output[execution.Id.Value] = new(
                new(result.StandardOutput.Value),
                new(result.StandardError.Value),
                result.IsOutputTruncated,
                result.IsErrorTruncated);
            TrimOutput();
            try
            {
                await store.CompleteAsync(new(
                    execution.Id,
                    state,
                    timeProvider.GetUtcNow(),
                    result.ExitCode,
                    result.DurationMilliseconds,
                    result.ErrorCode,
                    result.Error), CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Could not complete developer project execution {ExecutionId}.",
                    execution.Id.Value);
            }
        }
        finally
        {
            active.TryRemove(execution.Id.Value, out _);
            cancellation.Dispose();
            executionSlots.Release();
        }
    }

    private async ValueTask<Resolution> ResolveAsync(
        WorkbenchWorkspaceRequest request,
        WorkbenchExecutionTarget target,
        CancellationToken cancellationToken)
    {
        if (target is null || target.Kind is not WorkbenchExecutionTargetKind.ProjectEntryPoint ||
            string.IsNullOrWhiteSpace(target.ProjectPath.Value) ||
            string.IsNullOrWhiteSpace(target.SourcePath.Value) ||
            string.IsNullOrWhiteSpace(target.SourceBaseline.Value) ||
            string.IsNullOrWhiteSpace(target.DeclarationId.Value))
        {
            return Failure("invalid_execution_target",
                "A Roslyn-proven project entry point is required.");
        }
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request, cancellationToken);
        if (resolution.Error is not null || resolution.RootPath is null)
        {
            return Failure(resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The source context is unavailable.");
        }
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || workspace.Id != request.WorkspaceId.Value)
        {
            return Failure("workspace_not_active", "The requested workspace is not active.");
        }
        string entryPoint = Path.IsPathRooted(workspace.EntryPoint)
            ? Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint)
            : workspace.EntryPoint;
        WorkspaceDotNetInfo info = await dotNetInspector.InspectAsync(
            resolution.RootPath, entryPoint, cancellationToken);
        DotNetProjectInfo? project = info.Projects.FirstOrDefault(item =>
            item.Path.Equals(target.ProjectPath.Value, StringComparison.Ordinal));
        if (project is null)
        {
            return Failure("execution_project_unavailable",
                "The execution target project is not in the active inspected source context.");
        }
        if (target.TargetFramework.Value != "unknown" &&
            !project.TargetFrameworks.Contains(target.TargetFramework.Value, StringComparer.Ordinal))
        {
            return Failure("execution_framework_unavailable",
                "The execution target framework is not declared by the selected project.");
        }

        WorkspaceFileRead source = await fileReader.ReadAsync(
            resolution.RootPath, target.SourcePath.Value, cancellationToken);
        if (source.Error is not null || source.IsTruncated)
        {
            return Failure(source.ErrorCode ?? "execution_source_unavailable",
                source.Error ?? "The execution source could not be read completely.");
        }
        string hash = Convert.ToHexStringLower(SHA256.HashData(
            Utf8WithoutBom.GetBytes(source.Content)));
        if (!hash.Equals(target.SourceBaseline.Value, StringComparison.Ordinal))
        {
            return Failure("execution_source_changed",
                "The entry-point source changed after CodeLens discovery. Refresh and try again.");
        }
        return new(resolution.Context, resolution.RootPath, null, null);
    }

    private async ValueTask EnsureReconciledAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref reconciled) != 0)
        {
            return;
        }
        await reconciliationGate.WaitAsync(cancellationToken);
        try
        {
            if (reconciled == 0)
            {
                await store.InterruptRunningAsync(timeProvider.GetUtcNow(), cancellationToken);
                Volatile.Write(ref reconciled, 1);
            }
        }
        finally
        {
            reconciliationGate.Release();
        }
    }

    private DeveloperExecutionView Map(
        StoredDeveloperExecution execution,
        WorkbenchExecutionTarget target)
    {
        bool available = output.TryGetValue(execution.Id.Value, out TransientOutput? streams);
        return new(
            new(execution.Id.Value), new(execution.WorkspaceId.Value),
            execution.GoalId is null ? null : new(execution.GoalId.Value),
            execution.SourceDescription.Value, target, Map(execution.State),
            execution.StartedAt, execution.CompletedAt, execution.ExitCode,
            execution.DurationMilliseconds,
            streams?.StandardOutput, streams?.StandardError,
            streams?.IsOutputTruncated ?? false,
            streams?.IsErrorTruncated ?? false,
            available,
            execution.ErrorCode,
            execution.Error);
    }

    private static WorkbenchExecutionTarget Target(StoredDeveloperExecution execution) => new(
        WorkbenchExecutionTargetKind.ProjectEntryPoint,
        new(execution.ProjectPath.Value),
        new(execution.TargetFramework?.Value ?? "unknown"),
        new(execution.DeclarationId.Value),
        new(string.Empty),
        new(string.Empty),
        new(0));

    private void TrimOutput()
    {
        if (output.Count <= MaximumExecutions)
        {
            return;
        }
        HashSet<string> activeIds = active.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (string key in output.Keys.Where(key => !activeIds.Contains(key))
                     .Take(output.Count - MaximumExecutions))
        {
            output.TryRemove(key, out _);
        }
    }

    private static DeveloperExecutionState Map(StoredDeveloperExecutionState state) => state switch
    {
        StoredDeveloperExecutionState.Running => DeveloperExecutionState.Running,
        StoredDeveloperExecutionState.Succeeded => DeveloperExecutionState.Succeeded,
        StoredDeveloperExecutionState.Failed => DeveloperExecutionState.Failed,
        StoredDeveloperExecutionState.Cancelled => DeveloperExecutionState.Cancelled,
        StoredDeveloperExecutionState.Interrupted => DeveloperExecutionState.Interrupted,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static Resolution Failure(string code, string error) => new(null, null, code, error);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (CancellationTokenSource cancellation in active.Values)
        {
            cancellation.Cancel();
        }
    }

    private sealed record Resolution(
        WorkbenchWorkspaceContext? Context,
        string? RootPath,
        string? ErrorCode,
        string? Error);

    private sealed record TransientOutput(
        DeveloperExecutionOutput StandardOutput,
        DeveloperExecutionOutput StandardError,
        bool IsOutputTruncated,
        bool IsErrorTruncated);
}
