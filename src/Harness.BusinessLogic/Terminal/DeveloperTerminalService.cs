using System.Collections.Concurrent;
using System.Collections.Immutable;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Terminal;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Terminal;

internal sealed partial class DeveloperTerminalService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IDeveloperTerminalConnectionFactory connectionFactory,
    IDeveloperTerminalSessionStore sessionStore,
    TimeProvider timeProvider,
    ILogger<DeveloperTerminalService> logger) : IDeveloperTerminalService, IAsyncDisposable
{
    private const int MaximumLiveSessions = 4;
    private const int ReadBufferBytes = 16 * 1024;
    private const int MaximumWriteBytes = 64 * 1024;
    private readonly ConcurrentDictionary<string, ActiveSession> sessions =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim startGate = new(1, 1);
    private int disposed;

    public async ValueTask<DeveloperTerminalStartResult> StartAsync(
        DeveloperTerminalStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Volatile.Read(ref disposed) != 0)
        {
            return StartFailure("terminal_service_stopped", "The terminal service is stopping.");
        }

        if (!Valid(request.Dimensions))
        {
            return StartFailure("invalid_terminal_size",
                "Terminal size must be 20-400 columns and 5-200 rows.");
        }

        await startGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return StartFailure("terminal_service_stopped", "The terminal service is stopping.");
            }

            await EnsureReconciledAsync(cancellationToken);

            if (sessions.Values.Count(session => session.View.State ==
                    DeveloperTerminalSessionState.Running) >= MaximumLiveSessions)
            {
                return StartFailure("terminal_limit_reached",
                    $"Stop a terminal before opening more than {MaximumLiveSessions} sessions.");
            }

            WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
                request.Workspace,
                cancellationToken);
            if (resolution.RootPath is null)
            {
                return StartFailure(
                    resolution.ErrorCode ?? "workspace_unavailable",
                    resolution.Error ?? "The trusted workspace context is unavailable.");
            }

            StoredTerminalShell shell;
            DeveloperTerminalSessionId id = new(Guid.NewGuid().ToString("N"));
            try
            {
                shell = await connectionFactory.ResolveDefaultShellAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Developer terminal creation failed");
                return StartFailure("terminal_start_failed",
                    "The interactive shell could not be started.");
            }

            DeveloperTerminalSessionView view = new(
                id,
                resolution.Context.WorkspaceId,
                resolution.Context,
                new("."),
                new(shell.DisplayName.Value),
                new("Inherited host environment with locked terminal policy"),
                new("Transient · never included in model context"),
                request.Dimensions,
                DeveloperTerminalSessionState.Running,
                timeProvider.GetUtcNow(),
                CompletedAt: null,
                ExitCode: null,
                IsTrusted: true,
                ErrorCode: null,
                Error: null);

            try
            {
                await PersistStartAsync(view, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Developer terminal metadata creation failed");
                return StartFailure("terminal_persistence_failed",
                    "The terminal lifecycle could not be recorded, so no shell was started.");
            }

            IDeveloperTerminalConnection connection;
            try
            {
                connection = await connectionFactory.StartAsync(
                    new(
                        new(id.Value),
                        shell,
                        new(resolution.RootPath),
                        TerminalEnvironment(),
                        new(request.Dimensions.Columns, request.Dimensions.Rows)),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await PersistCompletionSafelyAsync(view with
                {
                    State = DeveloperTerminalSessionState.Failed,
                    CompletedAt = timeProvider.GetUtcNow(),
                    ErrorCode = "terminal_start_cancelled",
                    Error = "Terminal creation was cancelled before the shell started.",
                });
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Developer terminal creation failed");
                await PersistCompletionSafelyAsync(view with
                {
                    State = DeveloperTerminalSessionState.Failed,
                    CompletedAt = timeProvider.GetUtcNow(),
                    ErrorCode = "terminal_start_failed",
                    Error = "The interactive shell could not be started.",
                });
                return StartFailure("terminal_start_failed",
                    "The interactive shell could not be started.");
            }

            ActiveSession active = new(connection, view);
            if (!sessions.TryAdd(id.Value, active))
            {
                await connection.DisposeAsync();
                await PersistCompletionSafelyAsync(view with
                {
                    State = DeveloperTerminalSessionState.Failed,
                    CompletedAt = timeProvider.GetUtcNow(),
                    ErrorCode = "terminal_identity_conflict",
                    Error = "The terminal session identity could not be reserved.",
                });
                return StartFailure("terminal_identity_conflict",
                    "The terminal session identity could not be reserved.");
            }

            active.Observer = ObserveExitAsync(active);
            return new(active.View, null, null);
        }
        finally
        {
            startGate.Release();
        }
    }

    public async ValueTask<DeveloperTerminalListResult> ListAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureReconciledAsync(cancellationToken);
        IReadOnlyList<StoredTerminalSession> stored = await sessionStore.ListAsync(
            new(request.WorkspaceId.Value),
            request.GoalId is null ? null : new(request.GoalId.Value),
            20,
            cancellationToken);
        Dictionary<string, DeveloperTerminalSessionView> listed = stored
            .Select(Map)
            .ToDictionary(item => item.Id.Value, StringComparer.Ordinal);
        foreach (DeveloperTerminalSessionView live in sessions.Values.Select(item => item.View)
                     .Where(item => item.WorkspaceId == request.WorkspaceId &&
                                    item.SourceContext.GoalId == request.GoalId))
        {
            listed[live.Id.Value] = live;
        }

        return new(listed.Values.OrderByDescending(item => item.StartedAt).ToArray());
    }

    public async ValueTask<DeveloperTerminalSessionResult> GetAsync(
        DeveloperTerminalSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureReconciledAsync(cancellationToken);
        if (sessions.TryGetValue(sessionId.Value, out ActiveSession? active))
        {
            return new(active.View, null, null);
        }

        StoredTerminalSession? stored = await sessionStore.GetAsync(
            new(sessionId.Value), cancellationToken);
        return stored is null
            ? SessionFailure("terminal_not_found", "The terminal session is unavailable.")
            : new(Map(stored), null, null);
    }

    public async ValueTask<DeveloperTerminalReadResult> ReadAsync(
        DeveloperTerminalSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!sessions.TryGetValue(sessionId.Value, out ActiveSession? active))
        {
            return new(new(ReadOnlyMemory<byte>.Empty), true,
                "terminal_not_found", "The terminal session is unavailable.");
        }

        try
        {
            StoredTerminalReadResult result = await active.Connection.ReadAsync(
                ReadBufferBytes,
                cancellationToken);
            return new(new(result.Data.Value), result.EndOfStream, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Developer terminal read ended");
            return new(new(ReadOnlyMemory<byte>.Empty), true,
                "terminal_read_ended", "The terminal output stream ended.");
        }
    }

    public async ValueTask<DeveloperTerminalSessionResult> WriteAsync(
        DeveloperTerminalSessionId sessionId,
        DeveloperTerminalData data,
        CancellationToken cancellationToken = default)
    {
        if (data.Value.IsEmpty || data.Value.Length > MaximumWriteBytes)
        {
            return SessionFailure("invalid_terminal_input",
                "Terminal input must contain 1-65536 bytes.");
        }

        if (!TryGetRunning(sessionId, out ActiveSession? active,
                out DeveloperTerminalSessionResult? failure))
        {
            return failure!;
        }
        ActiveSession running = active!;

        try
        {
            await running.Connection.WriteAsync(new(data.Value), cancellationToken);
            return new(running.View, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Developer terminal input failed");
            return SessionFailure("terminal_write_failed", "Terminal input could not be sent.");
        }
    }

    public async ValueTask<DeveloperTerminalSessionResult> ResizeAsync(
        DeveloperTerminalSessionId sessionId,
        DeveloperTerminalDimensions dimensions,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(dimensions))
        {
            return SessionFailure("invalid_terminal_size",
                "Terminal size must be 20-400 columns and 5-200 rows.");
        }

        if (!TryGetRunning(sessionId, out ActiveSession? active,
                out DeveloperTerminalSessionResult? failure))
        {
            return failure!;
        }
        ActiveSession running = active!;

        try
        {
            await running.Connection.ResizeAsync(
                new(dimensions.Columns, dimensions.Rows),
                cancellationToken);
            lock (running.Gate)
            {
                running.View = running.View with { Dimensions = dimensions };
            }
            try
            {
                await sessionStore.UpdateDimensionsAsync(
                    new(sessionId.Value),
                    new(dimensions.Columns, dimensions.Rows),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Developer terminal dimension metadata update failed");
            }
            return new(running.View, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Developer terminal resize failed");
            return SessionFailure("terminal_resize_failed", "The terminal could not be resized.");
        }
    }

    public async ValueTask<DeveloperTerminalSessionResult> StopAsync(
        DeveloperTerminalSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!sessions.TryGetValue(sessionId.Value, out ActiveSession? active))
        {
            return SessionFailure("terminal_not_found", "The terminal session is unavailable.");
        }

        bool shouldStop;
        lock (active.Gate)
        {
            shouldStop = active.View.State == DeveloperTerminalSessionState.Running;
            active.StopRequested |= shouldStop;
        }

        if (!shouldStop)
        {
            return new(active.View, null, null);
        }

        try
        {
            await active.Connection.StopAsync(cancellationToken);
            await active.Connection.WaitForExitAsync(CancellationToken.None);
            await active.Observer;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Developer terminal stop failed");
            lock (active.Gate)
            {
                active.View = active.View with
                {
                    State = DeveloperTerminalSessionState.Failed,
                    CompletedAt = timeProvider.GetUtcNow(),
                    ErrorCode = "terminal_stop_failed",
                    Error = "The terminal process tree could not be stopped.",
                };
            }
            await PersistCompletionSafelyAsync(active.View);
        }

        return new(active.View, null, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await startGate.WaitAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(sessions.Values.Select(DisposeSessionAsync));
        }
        finally
        {
            startGate.Release();
            startGate.Dispose();
        }
    }

    private async Task ObserveExitAsync(ActiveSession active)
    {
        try
        {
            StoredTerminalExit exit = await active.Connection.WaitForExitAsync(CancellationToken.None);
            lock (active.Gate)
            {
                if (active.View.State != DeveloperTerminalSessionState.Running)
                {
                    return;
                }

                active.View = active.View with
                {
                    State = active.StopRequested
                        ? DeveloperTerminalSessionState.Stopped
                        : DeveloperTerminalSessionState.Exited,
                    CompletedAt = timeProvider.GetUtcNow(),
                    ExitCode = exit.ExitCode,
                };
            }
            await PersistCompletionSafelyAsync(active.View);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Developer terminal exit observation failed");
            lock (active.Gate)
            {
                active.View = active.View with
                {
                    State = DeveloperTerminalSessionState.Failed,
                    CompletedAt = timeProvider.GetUtcNow(),
                    ErrorCode = "terminal_observation_failed",
                    Error = "The terminal lifecycle could not be observed.",
                };
            }
            await PersistCompletionSafelyAsync(active.View);
        }
    }

    private async Task DisposeSessionAsync(ActiveSession active)
    {
        try
        {
            lock (active.Gate)
            {
                active.StopRequested = true;
            }

            await active.Connection.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Developer terminal shutdown stop failed");
        }
        finally
        {
            await active.Connection.DisposeAsync();
        }

        await active.Observer;
    }

    private bool TryGetRunning(
        DeveloperTerminalSessionId id,
        out ActiveSession? active,
        out DeveloperTerminalSessionResult? failure)
    {
        if (!sessions.TryGetValue(id.Value, out active))
        {
            failure = SessionFailure("terminal_not_found", "The terminal session is unavailable.");
            return false;
        }

        if (active.View.State != DeveloperTerminalSessionState.Running)
        {
            failure = SessionFailure("terminal_not_running", "The terminal session is no longer running.");
            return false;
        }

        failure = null;
        return true;
    }

    private static ImmutableArray<StoredTerminalEnvironmentVariable> TerminalEnvironment() =>
    [
        new(new("TERM"), new("xterm-256color")),
        new(new("COLORTERM"), new("truecolor")),
        new(new("DOTNET_CLI_TELEMETRY_OPTOUT"), new("1")),
        new(new("DOTNET_NOLOGO"), new("1")),
    ];

    private static bool Valid(DeveloperTerminalDimensions dimensions) =>
        dimensions.Columns is >= 20 and <= 400 && dimensions.Rows is >= 5 and <= 200;

    private static DeveloperTerminalStartResult StartFailure(string code, string error) =>
        new(null, code, error);

    private static DeveloperTerminalSessionResult SessionFailure(string code, string error) =>
        new(null, code, error);

    private sealed class ActiveSession(
        IDeveloperTerminalConnection connection,
        DeveloperTerminalSessionView view)
    {
        private DeveloperTerminalSessionView view = view;

        public object Gate { get; } = new();
        public IDeveloperTerminalConnection Connection { get; } = connection;
        public DeveloperTerminalSessionView View
        {
            get => Volatile.Read(ref view);
            set => Volatile.Write(ref view, value);
        }
        public bool StopRequested { get; set; }
        public Task Observer { get; set; } = Task.CompletedTask;
    }
}
