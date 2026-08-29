using System.Collections.Immutable;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Debugging;

public sealed record DeveloperDebugSessionId(string Value);
public sealed record DeveloperDebugSourcePath(string Value);
public sealed record DeveloperDebugLineNumber(int Value);
public sealed record DeveloperDebugThreadId(int Value);
public sealed record DeveloperDebugStackFrameId(int Value);
public sealed record DeveloperDebugVariablesReference(int Value);
public sealed record DeveloperDebugOutput(string Value);
public sealed record DeveloperDebugVariableName(string Value);
public sealed record DeveloperDebugVariableValue(string Value);
public sealed record DeveloperDebugVariableType(string Value);

public sealed record DeveloperDebugBreakpointLocation(
    DeveloperDebugSourcePath Source,
    DeveloperDebugLineNumber Line);

public sealed record DeveloperDebugBreakpoint(
    DeveloperDebugBreakpointLocation Location,
    bool IsVerified,
    DeveloperDebugLineNumber? ActualLine,
    string? Message);

public enum DeveloperDebugSessionState
{
    Configuring,
    Running,
    Stopped,
    Succeeded,
    Failed,
    Terminated,
    Interrupted,
}

public enum DeveloperDebugStopReason
{
    None,
    Entry,
    Breakpoint,
    Step,
    Pause,
    Exception,
    Unknown,
}

public enum DeveloperDebugCommand
{
    Continue,
    Pause,
    StepOver,
    StepIn,
    StepOut,
}

public sealed record DeveloperDebugThread(
    DeveloperDebugThreadId Id,
    string Name);

public sealed record DeveloperDebugStackFrame(
    DeveloperDebugStackFrameId Id,
    string Name,
    DeveloperDebugSourcePath? Source,
    DeveloperDebugLineNumber? Line,
    int? Column);

public sealed record DeveloperDebugScope(
    string Name,
    DeveloperDebugVariablesReference VariablesReference,
    bool IsExpensive);

public sealed record DeveloperDebugVariable(
    DeveloperDebugVariableName Name,
    DeveloperDebugVariableValue Value,
    DeveloperDebugVariableType? Type,
    DeveloperDebugVariablesReference VariablesReference,
    int? NamedVariables,
    int? IndexedVariables);

public sealed record DeveloperDebugSessionView(
    DeveloperDebugSessionId Id,
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    string SourceDescription,
    DeveloperProjectTarget Project,
    WorkbenchExecutionTarget Target,
    DeveloperDebugSessionState State,
    DeveloperDebugStopReason StopReason,
    DeveloperDebugThreadId? StoppedThreadId,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    ImmutableArray<DeveloperDebugBreakpoint> Breakpoints,
    ImmutableArray<DeveloperDebugThread> Threads,
    ImmutableArray<DeveloperDebugStackFrame> Stack,
    DeveloperDebugOutput Output,
    bool IsOutputTruncated);

public sealed record DeveloperDebugStartRequest(
    WorkbenchWorkspaceRequest Workspace,
    WorkbenchExecutionTarget Target,
    ImmutableArray<DeveloperDebugBreakpointLocation> Breakpoints,
    DeveloperRunOverrides? RunOverrides = null,
    bool StopAtEntry = false);

public sealed record DeveloperDebugStartResult(
    DeveloperDebugSessionView? Session,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperDebugSessionResult(
    DeveloperDebugSessionView? Session,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperDebugInspectionResult<T>(
    IReadOnlyList<T> Items,
    string? ErrorCode,
    string? Error);

public interface IDeveloperDebuggerService
{
    ValueTask<DeveloperDebugStartResult> StartAsync(
        DeveloperDebugStartRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperDebugSessionResult> GetAsync(
        DeveloperDebugSessionId sessionId,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperDebugSessionResult> CommandAsync(
        DeveloperDebugSessionId sessionId,
        DeveloperDebugCommand command,
        DeveloperDebugThreadId threadId,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperDebugSessionResult> StopAsync(
        DeveloperDebugSessionId sessionId,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperDebugInspectionResult<DeveloperDebugScope>> GetScopesAsync(
        DeveloperDebugSessionId sessionId,
        DeveloperDebugStackFrameId frameId,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperDebugInspectionResult<DeveloperDebugVariable>> GetVariablesAsync(
        DeveloperDebugSessionId sessionId,
        DeveloperDebugVariablesReference variablesReference,
        CancellationToken cancellationToken = default);
}
