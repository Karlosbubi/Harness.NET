using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Debugging;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Debugging;
using Harness.DataAccess.Inspection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.BusinessLogic.Tests.Debugging;

public sealed class DeveloperDebuggerServiceTests
{
    [Fact]
    public async Task Owns_launch_breakpoint_stop_inspection_step_and_termination_lifecycle()
    {
        FakeSession adapter = new();
        DeveloperDebuggerService service = CreateService(adapter);

        DeveloperDebugStartResult started = await service.StartAsync(new(
            WorkspaceRequest(),
            Target(),
            [new(new("Program.cs"), new(12))]));
        DeveloperDebugSessionView stopped = await WaitForStateAsync(
            service, started.Session!.Id, DeveloperDebugSessionState.Stopped);
        DeveloperDebugInspectionResult<DeveloperDebugScope> scopes =
            await service.GetScopesAsync(stopped.Id, stopped.Stack[0].Id);
        DeveloperDebugInspectionResult<DeveloperDebugVariable> variables =
            await service.GetVariablesAsync(stopped.Id, scopes.Items[0].VariablesReference);
        DeveloperDebugSessionResult stepping = await service.CommandAsync(
            stopped.Id, DeveloperDebugCommand.StepOver, stopped.StoppedThreadId!);
        DeveloperDebugSessionResult terminated = await service.StopAsync(stopped.Id);

        Assert.Null(started.Error);
        Assert.Equal(DeveloperDebugStopReason.Breakpoint, stopped.StopReason);
        Assert.Equal("Main Thread", Assert.Single(stopped.Threads).Name);
        Assert.Equal("Program.Main()", Assert.Single(stopped.Stack).Name);
        Assert.True(Assert.Single(stopped.Breakpoints).IsVerified);
        Assert.Equal("Locals", Assert.Single(scopes.Items).Name);
        Assert.Equal("answer", Assert.Single(variables.Items).Name.Value);
        Assert.Equal("42", variables.Items[0].Value.Value);
        Assert.Equal(DeveloperDebugSessionState.Running, stepping.Session?.State);
        Assert.Equal(DeveloperDebugSessionState.Terminated, terminated.Session?.State);
        Assert.True(adapter.ConfigurationCompleted);
        Assert.True(adapter.Disconnected);
    }

    [Fact]
    public async Task Refuses_launch_until_the_managed_adapter_is_verified()
    {
        FakeSession adapter = new();
        DeveloperDebuggerService service = CreateService(adapter, ready: false);

        DeveloperDebugStartResult result = await service.StartAsync(new(
            WorkspaceRequest(), Target(), []));

        Assert.Null(result.Session);
        Assert.Equal("debugger_unavailable", result.ErrorCode);
        Assert.False(adapter.ConfigurationCompleted);
    }

    [Fact]
    public async Task Natural_termination_releases_and_disposes_the_adapter_lifecycle()
    {
        FakeSession adapter = new();
        DeveloperDebuggerService service = CreateService(adapter);
        DeveloperDebugStartResult started = await service.StartAsync(new(
            WorkspaceRequest(), Target(), []));

        adapter.CompleteNaturally(exitCode: 0);
        DeveloperDebugSessionView completed = await WaitForStateAsync(
            service, started.Session!.Id, DeveloperDebugSessionState.Succeeded);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!adapter.Disposed) await Task.Delay(10, timeout.Token);

        Assert.Equal(0, completed.ExitCode);
        Assert.True(adapter.Disconnected);
        Assert.True(adapter.Disposed);
    }

    private static DeveloperDebuggerService CreateService(
        FakeSession session,
        bool ready = true) => new(
        new TargetResolver(),
        new SessionFactory(session),
        new SettingsService(ready),
        TimeProvider.System,
        NullLogger<DeveloperDebuggerService>.Instance);

    private static WorkbenchWorkspaceRequest WorkspaceRequest() => new(
        new("workspace-1"), null);

    private static WorkbenchExecutionTarget Target() => new(
        WorkbenchExecutionTargetKind.ProjectEntryPoint,
        new("App.csproj"),
        new("net10.0"),
        new("Program.Main"),
        new("Program.cs"),
        new(new string('a', 64)),
        new(1));

    private static async Task<DeveloperDebugSessionView> WaitForStateAsync(
        IDeveloperDebuggerService service,
        DeveloperDebugSessionId id,
        DeveloperDebugSessionState state)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (true)
        {
            DeveloperDebugSessionResult current = await service.GetAsync(id, timeout.Token);
            if (current.Session?.State == state) return current.Session;
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TargetResolver : IDeveloperExecutionTargetResolver
    {
        public ValueTask<DeveloperExecutionTargetResolution> ResolveDebugTargetAsync(
            WorkbenchWorkspaceRequest workspace,
            WorkbenchExecutionTarget target,
            DeveloperRunOverrides? runOverrides,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DeveloperExecutionTargetResolution>(new(
            new(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace,
                "Original workspace · user-editable source context"),
            "/workspace",
            new("App.csproj", "Microsoft.NET.Sdk", ["net10.0"], null, "enable", []),
            null,
            null));
    }

    private sealed class SettingsService(bool ready) : IDeveloperDebuggerSettingsService
    {
        public DebugAdapterStatus Current { get; } = new(
            ready ? DebugAdapterAvailability.Ready : DebugAdapterAvailability.NotInstalled,
            new("3.2.0-1092"), new("linux-x64"), ready ? "Ready." : "Absent.",
            !ready, ready);

        public ValueTask<DebugAdapterStatus> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);

        public ValueTask<DebugAdapterStatus> InstallAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);

        public ValueTask<DebugAdapterStatus> RemoveAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);
    }

    private sealed class SessionFactory(FakeSession session) : IDotNetDebugSessionFactory
    {
        public ValueTask<IDebugAdapterSession> StartLaunchAsync(
            string sourceRoot,
            StoredDotNetDebugLaunchRequest request,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("/workspace", sourceRoot);
            Assert.Equal("App.csproj", request.ProjectPath.Value);
            return ValueTask.FromResult<IDebugAdapterSession>(session);
        }
    }

    private sealed class FakeSession : IDebugAdapterSession
    {
        private readonly Channel<StoredDebugEvent> events = Channel.CreateUnbounded<StoredDebugEvent>();

        public StoredDebugSessionId Id { get; } = new("adapter-session");
        public StoredDebugAdapterCapabilities Capabilities { get; } = new(true, true, true, true);
        internal bool ConfigurationCompleted { get; private set; }
        internal bool Disconnected { get; private set; }
        internal bool Disposed { get; private set; }

        public async IAsyncEnumerable<StoredDebugEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (StoredDebugEvent value in events.Reader.ReadAllAsync(cancellationToken))
                yield return value;
        }

        public ValueTask<IReadOnlyList<StoredDebugBreakpoint>> SetBreakpointsAsync(
            StoredDebugSourcePath source,
            IReadOnlyList<StoredDebugBreakpointRequest> breakpoints,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<
                IReadOnlyList<StoredDebugBreakpoint>>(breakpoints.Select(item =>
                new StoredDebugBreakpoint(1, true, source, item.Line, item.Line, null)).ToArray());

        public ValueTask CompleteConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            ConfigurationCompleted = true;
            events.Writer.TryWrite(new(
                StoredDebugEventKind.Stopped,
                StoredDebugStopReason.Breakpoint,
                new(7), null, null, true));
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<StoredDebugThread>> GetThreadsAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult<
                IReadOnlyList<StoredDebugThread>>([new(new(7), "Main Thread")]);

        public ValueTask<IReadOnlyList<StoredDebugStackFrame>> GetStackTraceAsync(
            StoredDebugThreadId threadId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<
                IReadOnlyList<StoredDebugStackFrame>>(
                [new(new(8), "Program.Main()", new("Program.cs"), new(12), 5)]);

        public ValueTask<IReadOnlyList<StoredDebugScope>> GetScopesAsync(
            StoredDebugStackFrameId frameId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<
                IReadOnlyList<StoredDebugScope>>([new("Locals", new(9), false)]);

        public ValueTask<IReadOnlyList<StoredDebugVariable>> GetVariablesAsync(
            StoredDebugVariablesReference variablesReference,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<
                IReadOnlyList<StoredDebugVariable>>(
                [new(new("answer"), new("42"), new("int"), new(0), 0, 0)]);

        public ValueTask ContinueAsync(StoredDebugThreadId threadId,
            CancellationToken cancellationToken = default) => RunningAsync();

        public ValueTask PauseAsync(StoredDebugThreadId threadId,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask StepOverAsync(StoredDebugThreadId threadId,
            CancellationToken cancellationToken = default) => RunningAsync();

        public ValueTask StepInAsync(StoredDebugThreadId threadId,
            CancellationToken cancellationToken = default) => RunningAsync();

        public ValueTask StepOutAsync(StoredDebugThreadId threadId,
            CancellationToken cancellationToken = default) => RunningAsync();

        public ValueTask DisconnectAsync(bool terminateDebuggee,
            CancellationToken cancellationToken = default)
        {
            Disconnected = true;
            events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        internal void CompleteNaturally(int exitCode)
        {
            events.Writer.TryWrite(new(
                StoredDebugEventKind.Exited, StoredDebugStopReason.None,
                null, null, exitCode, false));
            events.Writer.TryWrite(new(
                StoredDebugEventKind.Terminated, StoredDebugStopReason.None,
                null, null, null, false));
        }

        private ValueTask RunningAsync()
        {
            events.Writer.TryWrite(new(
                StoredDebugEventKind.Continued, StoredDebugStopReason.None,
                new(7), null, null, false));
            return ValueTask.CompletedTask;
        }
    }
}
