using System.Collections.Immutable;

namespace Harness.DataAccess.Terminal;

public sealed record StoredTerminalSessionId(string Value);
public sealed record StoredTerminalExecutable(string Value);
public sealed record StoredTerminalShellName(string Value);
public sealed record StoredTerminalArgument(string Value);
public sealed record StoredTerminalWorkingDirectory(string Value);
public sealed record StoredTerminalEnvironmentName(string Value);
public sealed record StoredTerminalEnvironmentValue(string Value);
public sealed record StoredTerminalData(ReadOnlyMemory<byte> Value);
public sealed record StoredTerminalWorkspaceId(string Value);
public sealed record StoredTerminalGoalId(string Value);
public sealed record StoredTerminalSourceDescription(string Value);
public sealed record StoredTerminalSourceBranch(string Value);

public sealed record StoredTerminalEnvironmentVariable(
    StoredTerminalEnvironmentName Name,
    StoredTerminalEnvironmentValue Value);

public sealed record StoredTerminalDimensions(int Columns, int Rows);

public sealed record StoredTerminalShell(
    StoredTerminalExecutable Executable,
    StoredTerminalShellName DisplayName,
    ImmutableArray<StoredTerminalArgument> Arguments);

public sealed record StoredTerminalStartRequest(
    StoredTerminalSessionId SessionId,
    StoredTerminalShell Shell,
    StoredTerminalWorkingDirectory WorkingDirectory,
    ImmutableArray<StoredTerminalEnvironmentVariable> Environment,
    StoredTerminalDimensions Dimensions);

public sealed record StoredTerminalReadResult(
    StoredTerminalData Data,
    bool EndOfStream);

public sealed record StoredTerminalExit(int ExitCode);

public enum StoredTerminalSourceScope
{
    OriginalWorkspace,
    ApprovedGoalWorktree,
}

public enum StoredTerminalEnvironmentProfile
{
    InheritedLocked,
}

public enum StoredTerminalContentPolicy
{
    Transient,
}

public enum StoredTerminalSessionState
{
    Running,
    Exited,
    Stopped,
    Failed,
    Interrupted,
}

public sealed record StoredTerminalSession(
    StoredTerminalSessionId Id,
    StoredTerminalWorkspaceId WorkspaceId,
    StoredTerminalGoalId? GoalId,
    StoredTerminalSourceScope SourceScope,
    StoredTerminalSourceBranch? SourceBranch,
    StoredTerminalSourceDescription SourceDescription,
    StoredTerminalWorkingDirectory WorkingDirectory,
    StoredTerminalShellName Shell,
    StoredTerminalEnvironmentProfile EnvironmentProfile,
    StoredTerminalContentPolicy ContentPolicy,
    StoredTerminalDimensions Dimensions,
    StoredTerminalSessionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    string? ErrorCode,
    string? Error);

public sealed record StoredTerminalSessionStart(
    StoredTerminalSessionId Id,
    StoredTerminalWorkspaceId WorkspaceId,
    StoredTerminalGoalId? GoalId,
    StoredTerminalSourceScope SourceScope,
    StoredTerminalSourceBranch? SourceBranch,
    StoredTerminalSourceDescription SourceDescription,
    StoredTerminalWorkingDirectory WorkingDirectory,
    StoredTerminalShellName Shell,
    StoredTerminalEnvironmentProfile EnvironmentProfile,
    StoredTerminalContentPolicy ContentPolicy,
    StoredTerminalDimensions Dimensions,
    DateTimeOffset StartedAt);

public sealed record StoredTerminalSessionCompletion(
    StoredTerminalSessionId Id,
    StoredTerminalSessionState State,
    DateTimeOffset CompletedAt,
    int? ExitCode,
    string? ErrorCode,
    string? Error);

public interface IDeveloperTerminalSessionStore
{
    ValueTask<StoredTerminalSession> StartAsync(
        StoredTerminalSessionStart session,
        CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(
        StoredTerminalSessionCompletion completion,
        CancellationToken cancellationToken = default);

    ValueTask UpdateDimensionsAsync(
        StoredTerminalSessionId sessionId,
        StoredTerminalDimensions dimensions,
        CancellationToken cancellationToken = default);

    ValueTask<StoredTerminalSession?> GetAsync(
        StoredTerminalSessionId sessionId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredTerminalSession>> ListAsync(
        StoredTerminalWorkspaceId workspaceId,
        StoredTerminalGoalId? goalId,
        int maximumResults,
        CancellationToken cancellationToken = default);

    ValueTask<int> InterruptRunningAsync(
        DateTimeOffset completedAt,
        DateTimeOffset startedBefore,
        CancellationToken cancellationToken = default);
}

public interface IDeveloperTerminalConnection : IAsyncDisposable
{
    ValueTask<StoredTerminalReadResult> ReadAsync(
        int maximumBytes,
        CancellationToken cancellationToken = default);

    ValueTask WriteAsync(
        StoredTerminalData data,
        CancellationToken cancellationToken = default);

    ValueTask ResizeAsync(
        StoredTerminalDimensions dimensions,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);

    ValueTask<StoredTerminalExit> WaitForExitAsync(
        CancellationToken cancellationToken = default);
}

public interface IDeveloperTerminalConnectionFactory
{
    ValueTask<StoredTerminalShell> ResolveDefaultShellAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IDeveloperTerminalConnection> StartAsync(
        StoredTerminalStartRequest request,
        CancellationToken cancellationToken = default);
}
