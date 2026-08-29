using System.Collections.Immutable;
using Harness.DataAccess.Execution;

namespace Harness.DataAccess.Debugging;

public sealed record StoredDebugSessionId(string Value);
public sealed record StoredDebugSourceRoot(string Value);
public sealed record StoredDebugWorkingDirectory(string Value);
public sealed record StoredDebugArgument(string Value);
public sealed record StoredDebugEnvironmentName(string Value);
public sealed record StoredDebugEnvironmentValue(string Value);
public sealed record StoredDebugProcessId(int Value);
public sealed record StoredDebugSourcePath(string Value);
public sealed record StoredDebugLineNumber(int Value);
public sealed record StoredDebugThreadId(int Value);
public sealed record StoredDebugStackFrameId(int Value);
public sealed record StoredDebugVariablesReference(int Value);
public sealed record StoredDebugVariableName(string Value);
public sealed record StoredDebugVariableValue(string Value);
public sealed record StoredDebugVariableType(string Value);

public sealed record StoredDebugEnvironmentEntry(
    StoredDebugEnvironmentName Name,
    StoredDebugEnvironmentValue Value);

public enum StoredDebugAdapterStartKind
{
    Launch,
    AttachOwnedProcess,
}

public sealed record StoredDebugAdapterStartRequest(
    StoredDebugSessionId SessionId,
    StoredDebugAdapterStartKind Kind,
    StoredDebugSourceRoot SourceRoot,
    StoredDebugWorkingDirectory WorkingDirectory,
    ImmutableArray<StoredDebugArgument> Arguments,
    ImmutableArray<StoredDebugEnvironmentEntry> Environment,
    StoredDebugProcessId? OwnedProcessId,
    bool StopAtEntry,
    bool JustMyCode);

public sealed record StoredDebugAdapterCapabilities(
    bool SupportsConfigurationDone,
    bool SupportsConditionalBreakpoints,
    bool SupportsTerminate,
    bool SupportsVariablePaging);

public sealed record StoredDebugBreakpointRequest(
    StoredDebugSourcePath Source,
    StoredDebugLineNumber Line,
    string? Condition = null);

public sealed record StoredDebugBreakpoint(
    int? AdapterId,
    bool IsVerified,
    StoredDebugSourcePath Source,
    StoredDebugLineNumber RequestedLine,
    StoredDebugLineNumber? ActualLine,
    string? Message);

public sealed record StoredDebugThread(
    StoredDebugThreadId Id,
    string Name);

public sealed record StoredDebugStackFrame(
    StoredDebugStackFrameId Id,
    string Name,
    StoredDebugSourcePath? Source,
    StoredDebugLineNumber? Line,
    int? Column);

public sealed record StoredDebugScope(
    string Name,
    StoredDebugVariablesReference VariablesReference,
    bool IsExpensive);

public sealed record StoredDebugVariable(
    StoredDebugVariableName Name,
    StoredDebugVariableValue Value,
    StoredDebugVariableType? Type,
    StoredDebugVariablesReference VariablesReference,
    int? NamedVariables,
    int? IndexedVariables);

public enum StoredDebugEventKind
{
    Initialized,
    Stopped,
    Continued,
    Output,
    Exited,
    Terminated,
    ThreadChanged,
    BreakpointChanged,
    AdapterFailed,
}

public enum StoredDebugStopReason
{
    None,
    Entry,
    Breakpoint,
    Step,
    Pause,
    Exception,
    Unknown,
}

public sealed record StoredDebugEvent(
    StoredDebugEventKind Kind,
    StoredDebugStopReason StopReason,
    StoredDebugThreadId? ThreadId,
    string? Message,
    int? ExitCode,
    bool AllThreadsStopped);

public interface IDebugAdapterSession : IAsyncDisposable
{
    StoredDebugSessionId Id { get; }

    StoredDebugAdapterCapabilities Capabilities { get; }

    IAsyncEnumerable<StoredDebugEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredDebugBreakpoint>> SetBreakpointsAsync(
        StoredDebugSourcePath source,
        IReadOnlyList<StoredDebugBreakpointRequest> breakpoints,
        CancellationToken cancellationToken = default);

    ValueTask CompleteConfigurationAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredDebugThread>> GetThreadsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredDebugStackFrame>> GetStackTraceAsync(
        StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredDebugScope>> GetScopesAsync(
        StoredDebugStackFrameId frameId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredDebugVariable>> GetVariablesAsync(
        StoredDebugVariablesReference variablesReference,
        CancellationToken cancellationToken = default);

    ValueTask ContinueAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default);

    ValueTask PauseAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default);

    ValueTask StepOverAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default);

    ValueTask StepInAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default);

    ValueTask StepOutAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default);

    ValueTask DisconnectAsync(bool terminateDebuggee,
        CancellationToken cancellationToken = default);
}

internal interface IDebugAdapterSessionFactory
{
    ValueTask<IDebugAdapterSession> StartAsync(
        StoredDebugAdapterStartRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record StoredDotNetDebugLaunchRequest(
    StoredDebugSessionId SessionId,
    DotNetProjectPath ProjectPath,
    DotNetTargetFramework? TargetFramework,
    DotNetConfigurationName? Configuration,
    DotNetRunOverrides? RunOverrides,
    bool StopAtEntry,
    bool JustMyCode);

public interface IDotNetDebugSessionFactory
{
    ValueTask<IDebugAdapterSession> StartLaunchAsync(
        string sourceRoot,
        StoredDotNetDebugLaunchRequest request,
        CancellationToken cancellationToken = default);
}
