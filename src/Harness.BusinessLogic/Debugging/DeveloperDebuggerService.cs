using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Harness.BusinessLogic.Execution;
using Harness.DataAccess.Debugging;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Debugging;

internal sealed class DeveloperDebuggerService(
    IDeveloperExecutionTargetResolver targetResolver,
    IDotNetDebugSessionFactory sessionFactory,
    IDotNetTestDebugSessionFactory testSessionFactory,
    IDeveloperDebuggerSettingsService settings,
    TimeProvider timeProvider,
    ILogger<DeveloperDebuggerService> logger) : IDeveloperDebuggerService, IAsyncDisposable
{
    private const int MaximumConcurrentSessions = 2;
    private const int MaximumRetainedSessions = 50;
    private const int MaximumBreakpoints = 256;
    private const int MaximumOutputCharacters = 256 * 1024;
    private readonly ConcurrentDictionary<string, SessionState> sessions = new();
    private readonly SemaphoreSlim sessionSlots = new(
        MaximumConcurrentSessions, MaximumConcurrentSessions);
    private bool disposed;

    public async ValueTask<DeveloperDebugStartResult> StartAsync(
        DeveloperDebugStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (settings.Current.Availability is not DebugAdapterAvailability.Ready)
            return new(null, "debugger_unavailable",
                "Install or repair the verified managed debugger in Settings first.");
        if (!ValidateBreakpoints(request.Breakpoints, out string? breakpointError))
            return new(null, "debug_breakpoints_invalid", breakpointError);
        if (!await sessionSlots.WaitAsync(0, cancellationToken))
            return new(null, "debug_session_limit_reached",
                $"At most {MaximumConcurrentSessions} debug sessions may be active.");

        bool slotTransferred = false;
        IDebugAdapterSession? adapter = null;
        try
        {
            DeveloperExecutionTargetResolution resolution = await targetResolver
                .ResolveDebugTargetAsync(request.Workspace, request.Target,
                    request.RunOverrides, cancellationToken);
            if (resolution.RootPath is null || resolution.Context is null)
                return new(null, resolution.ErrorCode, resolution.Error);

            DeveloperDebugSessionId id = new(Guid.NewGuid().ToString("N"));
            try
            {
                adapter = await sessionFactory.StartLaunchAsync(
                    resolution.RootPath,
                    new(
                        new(id.Value),
                        new(request.Target.ProjectPath.Value),
                        request.Target.TargetFramework.Value == "unknown"
                            ? null
                            : new(request.Target.TargetFramework.Value),
                        Configuration: null,
                        DeveloperProjectExecutionService.Map(request.RunOverrides),
                        request.StopAtEntry,
                        JustMyCode: true),
                    cancellationToken);
                ImmutableArray<DeveloperDebugBreakpoint> configured =
                    await ConfigureBreakpointsAsync(adapter, request.Breakpoints,
                        cancellationToken);
                await adapter.CompleteConfigurationAsync(cancellationToken);

                DeveloperProjectTarget project = new(
                    new(request.Target.ProjectPath.Value),
                    request.Target.TargetFramework.Value == "unknown"
                        ? null : new(request.Target.TargetFramework.Value),
                    null);
                DeveloperDebugSessionView view = new(
                    id,
                    request.Workspace.WorkspaceId,
                    resolution.Context.GoalId,
                    resolution.Context.Description,
                    project,
                    request.Target,
                    DeveloperDebugSessionState.Running,
                    DeveloperDebugStopReason.None,
                    null,
                    null,
                    timeProvider.GetUtcNow(),
                    null,
                    "Debugger running; waiting for a breakpoint or pause.",
                    configured,
                    [],
                    [],
                    new(string.Empty),
                    IsOutputTruncated: false);
                SessionState state = new(adapter, view);
                if (!sessions.TryAdd(id.Value, state))
                {
                    await DisposeAdapterAsync(adapter);
                    return new(null, "debug_session_identity_conflict",
                        "The debug session identity already exists.");
                }
                slotTransferred = true;
                state.Events = ObserveAsync(state);
                TrimSessions();
                return new(state.Snapshot(), null, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (adapter is not null) await DisposeAdapterAsync(adapter);
                logger.LogWarning(exception, "Could not start a managed debug session.");
                return new(null, "debug_start_failed", SafeError(exception));
            }
            catch
            {
                if (adapter is not null) await DisposeAdapterAsync(adapter);
                throw;
            }
        }
        finally
        {
            if (!slotTransferred) sessionSlots.Release();
        }
    }

    public async ValueTask<DeveloperDebugStartResult> StartTestAsync(
        DeveloperTestDebugStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (settings.Current.Availability is not DebugAdapterAvailability.Ready)
            return new(null, "debugger_unavailable",
                "Install or repair the verified managed debugger in Settings first.");
        if (!OperatingSystem.IsLinux())
            return new(null, "test_debug_platform_unsupported",
                "Owned Test Debug process discovery is currently supported on Linux.");
        if (!await sessionSlots.WaitAsync(0, cancellationToken))
            return new(null, "debug_session_limit_reached",
                $"At most {MaximumConcurrentSessions} debug sessions may be active.");

        bool slotTransferred = false;
        IDebugAdapterSession? adapter = null;
        try
        {
            DeveloperTestDebugTargetResolution resolution = await targetResolver
                .ResolveTestDebugTargetAsync(request.Workspace, request.Project,
                    request.Test, cancellationToken);
            if (resolution.RootPath is null || resolution.Context is null ||
                resolution.Source is null || resolution.Line is null)
            {
                return new(null, resolution.ErrorCode, resolution.Error);
            }

            DeveloperDebugSessionId id = new(Guid.NewGuid().ToString("N"));
            adapter = await testSessionFactory.StartAsync(resolution.RootPath, new(
                new(id.Value),
                new(request.Project.ProjectPath.Value),
                request.Project.TargetFramework is null
                    ? null : new(request.Project.TargetFramework.Value),
                request.Project.Configuration is null
                    ? null : new(request.Project.Configuration.Value),
                new(request.Test.FullyQualifiedName.Value),
                JustMyCode: true), cancellationToken);
            DeveloperDebugBreakpointLocation breakpoint = new(
                new(resolution.Source.Value), new(resolution.Line.Value));
            ImmutableArray<DeveloperDebugBreakpoint> configured =
                await ConfigureBreakpointsAsync(adapter, [breakpoint], cancellationToken);
            await adapter.CompleteConfigurationAsync(cancellationToken);
            DeveloperDebugSessionView view = new(
                id,
                request.Workspace.WorkspaceId,
                resolution.Context.GoalId,
                resolution.Context.Description,
                request.Project,
                Target: null,
                DeveloperDebugSessionState.Running,
                DeveloperDebugStopReason.None,
                null,
                null,
                timeProvider.GetUtcNow(),
                null,
                $"Debugging test {request.Test.FullyQualifiedName.Value}…",
                configured,
                [],
                [],
                new(string.Empty),
                IsOutputTruncated: false,
                Test: request.Test);
            SessionState state = new(adapter, view);
            if (!sessions.TryAdd(id.Value, state))
            {
                await DisposeAdapterAsync(adapter);
                return new(null, "debug_session_identity_conflict",
                    "The debug session identity already exists.");
            }
            slotTransferred = true;
            state.Events = ObserveAsync(state);
            TrimSessions();
            return new(state.Snapshot(), null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (adapter is not null) await DisposeAdapterAsync(adapter);
            logger.LogWarning(exception, "Could not start an owned Test Debug session.");
            return new(null, "test_debug_start_failed", SafeError(exception));
        }
        catch
        {
            if (adapter is not null) await DisposeAdapterAsync(adapter);
            throw;
        }
        finally
        {
            if (!slotTransferred) sessionSlots.Release();
        }
    }

    public ValueTask<DeveloperDebugSessionResult> GetAsync(
        DeveloperDebugSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(TryGet(sessionId, out SessionState state)
            ? new DeveloperDebugSessionResult(state.Snapshot(), null, null)
            : new(null, "debug_session_unavailable", "The debug session is unavailable."));
    }

    public async ValueTask<DeveloperDebugSessionResult> CommandAsync(
        DeveloperDebugSessionId sessionId,
        DeveloperDebugCommand command,
        DeveloperDebugThreadId threadId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(command) || threadId.Value <= 0 ||
            !TryGet(sessionId, out SessionState state))
            return new(null, "debug_command_invalid", "The debug command is invalid.");
        try
        {
            StoredDebugThreadId adapterThread = new(threadId.Value);
            switch (command)
            {
                case DeveloperDebugCommand.Continue:
                    await state.Adapter.ContinueAsync(adapterThread, cancellationToken);
                    break;
                case DeveloperDebugCommand.Pause:
                    await state.Adapter.PauseAsync(adapterThread, cancellationToken);
                    break;
                case DeveloperDebugCommand.StepOver:
                    await state.Adapter.StepOverAsync(adapterThread, cancellationToken);
                    break;
                case DeveloperDebugCommand.StepIn:
                    await state.Adapter.StepInAsync(adapterThread, cancellationToken);
                    break;
                case DeveloperDebugCommand.StepOut:
                    await state.Adapter.StepOutAsync(adapterThread, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command));
            }
            state.Update(view => view with
            {
                State = command is DeveloperDebugCommand.Pause
                    ? view.State : DeveloperDebugSessionState.Running,
                Status = command is DeveloperDebugCommand.Pause
                    ? "Pause requested…" : $"{CommandLabel(command)} requested…",
            });
            return new(state.Snapshot(), null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(state.Snapshot(), "debug_command_failed", SafeError(exception));
        }
    }

    public async ValueTask<DeveloperDebugSessionResult> StopAsync(
        DeveloperDebugSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!TryGet(sessionId, out SessionState state))
            return new(null, "debug_session_unavailable", "The debug session is unavailable.");
        try
        {
            await CloseSessionAsync(state, terminateDebuggee: true, cancellationToken);
            state.Update(view => view with
            {
                State = DeveloperDebugSessionState.Terminated,
                CompletedAt = timeProvider.GetUtcNow(),
                Status = "Debug session stopped.",
            });
            state.ReleaseSlot(sessionSlots);
            return new(state.Snapshot(), null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(state.Snapshot(), "debug_stop_failed", SafeError(exception));
        }
    }

    public async ValueTask<DeveloperDebugInspectionResult<DeveloperDebugScope>> GetScopesAsync(
        DeveloperDebugSessionId sessionId,
        DeveloperDebugStackFrameId frameId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGet(sessionId, out SessionState state) || frameId.Value <= 0)
            return new([], "debug_frame_invalid", "Select a valid stopped stack frame.");
        try
        {
            IReadOnlyList<StoredDebugScope> scopes = await state.Adapter.GetScopesAsync(
                new(frameId.Value), cancellationToken);
            return new(scopes.Select(item => new DeveloperDebugScope(
                item.Name, new(item.VariablesReference.Value), item.IsExpensive)).ToArray(),
                null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new([], "debug_scopes_failed", SafeError(exception));
        }
    }

    public async ValueTask<DeveloperDebugInspectionResult<DeveloperDebugVariable>>
        GetVariablesAsync(
            DeveloperDebugSessionId sessionId,
            DeveloperDebugVariablesReference variablesReference,
            CancellationToken cancellationToken = default)
    {
        if (!TryGet(sessionId, out SessionState state) || variablesReference.Value <= 0)
            return new([], "debug_variables_invalid", "Select an expandable debug value.");
        try
        {
            IReadOnlyList<StoredDebugVariable> variables = await state.Adapter.GetVariablesAsync(
                new(variablesReference.Value), cancellationToken);
            return new(variables.Select(item => new DeveloperDebugVariable(
                new(item.Name.Value), new(item.Value.Value),
                item.Type is null ? null : new(item.Type.Value),
                new(item.VariablesReference.Value), item.NamedVariables,
                item.IndexedVariables)).ToArray(), null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new([], "debug_variables_failed", SafeError(exception));
        }
    }

    private async Task ObserveAsync(SessionState state)
    {
        try
        {
            await foreach (StoredDebugEvent debugEvent in state.Adapter.ReadEventsAsync())
            {
                switch (debugEvent.Kind)
                {
                    case StoredDebugEventKind.Stopped:
                        await ApplyStoppedAsync(state, debugEvent);
                        break;
                    case StoredDebugEventKind.Continued:
                        state.Update(view => IsTerminal(view.State) ? view : view with
                            {
                                State = DeveloperDebugSessionState.Running,
                                StopReason = DeveloperDebugStopReason.None,
                                StoppedThreadId = null,
                                Threads = [],
                                Stack = [],
                                Status = "Debugger running…",
                            });
                        break;
                    case StoredDebugEventKind.Output:
                        state.AppendOutput(debugEvent.Message, MaximumOutputCharacters);
                        break;
                    case StoredDebugEventKind.Exited:
                        state.Update(view => IsTerminal(view.State) ? view : view with
                            {
                                ExitCode = debugEvent.ExitCode,
                                Status = $"Debuggee exited with code {debugEvent.ExitCode ?? -1}.",
                            });
                        break;
                    case StoredDebugEventKind.Terminated:
                        Complete(state);
                        await CloseSessionAsync(state, terminateDebuggee: false,
                            CancellationToken.None);
                        return;
                    case StoredDebugEventKind.AdapterFailed:
                        state.Update(view => view with
                        {
                            State = DeveloperDebugSessionState.Failed,
                            CompletedAt = timeProvider.GetUtcNow(),
                            Status = debugEvent.Message ?? "The debug adapter failed.",
                        });
                        state.ReleaseSlot(sessionSlots);
                        await CloseSessionAsync(state, terminateDebuggee: true,
                            CancellationToken.None);
                        return;
                    case StoredDebugEventKind.BreakpointChanged:
                        ApplyBreakpointChanged(state, debugEvent.Breakpoint);
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Debug session {SessionId} observation failed.",
                state.View.Id.Value);
            state.Update(view => view with
            {
                State = DeveloperDebugSessionState.Failed,
                CompletedAt = timeProvider.GetUtcNow(),
                Status = "The debug adapter connection failed.",
            });
            state.ReleaseSlot(sessionSlots);
            await CloseSessionAsync(state, terminateDebuggee: true,
                CancellationToken.None);
        }
    }

    private async Task ApplyStoppedAsync(SessionState state, StoredDebugEvent debugEvent)
    {
        IReadOnlyList<StoredDebugThread> threads = await state.Adapter.GetThreadsAsync();
        StoredDebugThreadId? stopped = debugEvent.ThreadId ?? threads.FirstOrDefault()?.Id;
        IReadOnlyList<StoredDebugStackFrame> stack = stopped is null
            ? [] : await state.Adapter.GetStackTraceAsync(stopped);
        state.Update(view => IsTerminal(view.State) ? view : view with
            {
                State = DeveloperDebugSessionState.Stopped,
                StopReason = Map(debugEvent.StopReason),
                StoppedThreadId = stopped is null ? null : new(stopped.Value),
                Threads = threads.Select(item => new DeveloperDebugThread(
                    new(item.Id.Value), item.Name)).ToImmutableArray(),
                Stack = stack.Select(item => new DeveloperDebugStackFrame(
                    new(item.Id.Value), item.Name,
                    item.Source is null ? null : new(item.Source.Value),
                    item.Line is null ? null : new(item.Line.Value),
                    item.Column)).ToImmutableArray(),
                Status = $"Stopped: {StopLabel(debugEvent.StopReason)}.",
            });
    }

    private void Complete(SessionState state)
    {
        state.Update(view => view with
        {
            State = view.ExitCode is 0 or null
                ? DeveloperDebugSessionState.Succeeded
                : DeveloperDebugSessionState.Failed,
            CompletedAt = timeProvider.GetUtcNow(),
            Status = view.ExitCode is 0 or null
                ? "Debug session completed."
                : $"Debug session failed with exit code {view.ExitCode}.",
        });
        state.ReleaseSlot(sessionSlots);
    }

    private static async ValueTask<ImmutableArray<DeveloperDebugBreakpoint>>
        ConfigureBreakpointsAsync(
            IDebugAdapterSession adapter,
            ImmutableArray<DeveloperDebugBreakpointLocation> breakpoints,
            CancellationToken cancellationToken)
    {
        ImmutableArray<DeveloperDebugBreakpoint>.Builder configured =
            ImmutableArray.CreateBuilder<DeveloperDebugBreakpoint>(breakpoints.Length);
        foreach (IGrouping<string, DeveloperDebugBreakpointLocation> group in breakpoints
                     .GroupBy(item => item.Source.Value, StringComparer.Ordinal))
        {
            StoredDebugSourcePath source = new(group.Key);
            IReadOnlyList<StoredDebugBreakpoint> result = await adapter.SetBreakpointsAsync(
                source,
                group.Select(item => new StoredDebugBreakpointRequest(
                    source, new(item.Line.Value))).ToArray(),
                cancellationToken);
            configured.AddRange(result.Select(item => new DeveloperDebugBreakpoint(
                new(new(item.Source.Value), new(item.RequestedLine.Value)),
                item.IsVerified,
                item.ActualLine is null ? null : new(item.ActualLine.Value),
                item.Message,
                item.AdapterId)));
        }
        return configured.ToImmutable();
    }

    private static void ApplyBreakpointChanged(
        SessionState state,
        StoredDebugBreakpoint? changed)
    {
        if (changed is null) return;
        state.Update(view => view with
        {
            Breakpoints = view.Breakpoints.Select(item =>
                (item.AdapterId is not null && item.AdapterId == changed.AdapterId) ||
                (item.Location.Source.Value.Equals(changed.Source.Value, StringComparison.Ordinal) &&
                 item.Location.Line.Value == changed.RequestedLine.Value)
                    ? item with
                    {
                        IsVerified = changed.IsVerified,
                        ActualLine = changed.ActualLine is null
                            ? null : new(changed.ActualLine.Value),
                        Message = changed.Message,
                        AdapterId = changed.AdapterId,
                    }
                    : item).ToImmutableArray(),
        });
    }

    private static bool ValidateBreakpoints(
        ImmutableArray<DeveloperDebugBreakpointLocation> breakpoints,
        out string? error)
    {
        error = null;
        if (breakpoints.IsDefault || breakpoints.Length > MaximumBreakpoints ||
            breakpoints.Any(item => string.IsNullOrWhiteSpace(item.Source.Value) ||
                item.Source.Value.Length > 1_024 || Path.IsPathRooted(item.Source.Value) ||
                item.Line.Value <= 0) ||
            breakpoints.Distinct().Count() != breakpoints.Length)
        {
            error = $"Select at most {MaximumBreakpoints} distinct confined source lines.";
            return false;
        }
        return true;
    }

    private bool TryGet(DeveloperDebugSessionId? id, out SessionState state)
    {
        state = null!;
        if (id is not { Value.Length: > 0 } ||
            !sessions.TryGetValue(id.Value, out SessionState? found))
        {
            return false;
        }
        state = found;
        return true;
    }

    private void TrimSessions()
    {
        if (sessions.Count <= MaximumRetainedSessions) return;
        foreach ((SessionState State, DeveloperDebugSessionView View) old in sessions.Values
                     .Select(item => (State: item, View: item.Snapshot()))
                     .Where(item => item.View.CompletedAt is not null)
                     .OrderBy(item => item.View.CompletedAt)
                     .Take(sessions.Count - MaximumRetainedSessions))
        {
            sessions.TryRemove(old.View.Id.Value, out _);
        }
    }

    private static DeveloperDebugStopReason Map(StoredDebugStopReason reason) => reason switch
    {
        StoredDebugStopReason.Entry => DeveloperDebugStopReason.Entry,
        StoredDebugStopReason.Breakpoint => DeveloperDebugStopReason.Breakpoint,
        StoredDebugStopReason.Step => DeveloperDebugStopReason.Step,
        StoredDebugStopReason.Pause => DeveloperDebugStopReason.Pause,
        StoredDebugStopReason.Exception => DeveloperDebugStopReason.Exception,
        StoredDebugStopReason.Unknown => DeveloperDebugStopReason.Unknown,
        _ => DeveloperDebugStopReason.None,
    };

    private static string StopLabel(StoredDebugStopReason reason) => reason switch
    {
        StoredDebugStopReason.Entry => "entry",
        StoredDebugStopReason.Breakpoint => "breakpoint",
        StoredDebugStopReason.Step => "step",
        StoredDebugStopReason.Pause => "pause",
        StoredDebugStopReason.Exception => "exception",
        _ => "debugger event",
    };

    private static string CommandLabel(DeveloperDebugCommand command) => command switch
    {
        DeveloperDebugCommand.Continue => "Continue",
        DeveloperDebugCommand.Pause => "Pause",
        DeveloperDebugCommand.StepOver => "Step over",
        DeveloperDebugCommand.StepIn => "Step in",
        DeveloperDebugCommand.StepOut => "Step out",
        _ => "Debug command",
    };

    private static bool IsTerminal(DeveloperDebugSessionState state) => state is
        DeveloperDebugSessionState.Succeeded or DeveloperDebugSessionState.Failed or
        DeveloperDebugSessionState.Terminated or DeveloperDebugSessionState.Interrupted;

    private static string SafeError(Exception exception) => exception.Message.Length <= 1_024
        ? exception.Message
        : exception.Message[..1_024];

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        foreach (SessionState state in sessions.Values)
        {
            state.ReleaseSlot(sessionSlots);
            await CloseSessionAsync(state, terminateDebuggee: true,
                CancellationToken.None);
        }
        sessionSlots.Dispose();
    }

    private async ValueTask CloseSessionAsync(
        SessionState state,
        bool terminateDebuggee,
        CancellationToken cancellationToken)
    {
        if (!state.TryBeginClose()) return;
        try
        {
            await state.Adapter.DisconnectAsync(terminateDebuggee, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Debug session {SessionId} disconnect failed during cleanup.",
                state.Snapshot().Id.Value);
        }
        finally
        {
            await DisposeAdapterAsync(state.Adapter);
        }
    }

    private static async ValueTask DisposeAdapterAsync(IDebugAdapterSession adapter)
    {
        try
        {
            await adapter.DisposeAsync();
        }
        catch
        {
            // Cleanup must not replace the original debugger result or exception.
        }
    }

    private sealed class SessionState(
        IDebugAdapterSession adapter,
        DeveloperDebugSessionView view)
    {
        private readonly Lock gate = new();
        private readonly StringBuilder output = new();
        private bool slotReleased;
        private bool closeStarted;

        internal IDebugAdapterSession Adapter { get; } = adapter;
        internal DeveloperDebugSessionView View { get; private set; } = view;
        internal Task? Events { get; set; }

        internal DeveloperDebugSessionView Snapshot()
        {
            lock (gate) return View;
        }

        internal void Update(Func<DeveloperDebugSessionView, DeveloperDebugSessionView> update)
        {
            lock (gate) View = update(View);
        }

        internal void AppendOutput(string? value, int maximumCharacters)
        {
            if (string.IsNullOrEmpty(value)) return;
            lock (gate)
            {
                int remaining = maximumCharacters - output.Length;
                if (remaining > 0) output.Append(value, 0, Math.Min(value.Length, remaining));
                View = View with
                {
                    Output = new(output.ToString()),
                    IsOutputTruncated = value.Length > remaining || View.IsOutputTruncated,
                };
            }
        }

        internal void ReleaseSlot(SemaphoreSlim slots)
        {
            lock (gate)
            {
                if (slotReleased) return;
                slotReleased = true;
                slots.Release();
            }
        }

        internal bool TryBeginClose()
        {
            lock (gate)
            {
                if (closeStarted) return false;
                closeStarted = true;
                return true;
            }
        }
    }
}
