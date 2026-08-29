using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Terminal;

public sealed record DeveloperTerminalSessionId(string Value);
public sealed record DeveloperTerminalShellName(string Value);
public sealed record DeveloperTerminalWorkingDirectory(string Value);
public sealed record DeveloperTerminalEnvironmentProfile(string Value);
public sealed record DeveloperTerminalContentPolicy(string Value);
public sealed record DeveloperTerminalData(ReadOnlyMemory<byte> Value);
public sealed record DeveloperTerminalDimensions(int Columns, int Rows);

public enum DeveloperTerminalSessionState
{
    Running,
    Exited,
    Stopped,
    Failed,
    Interrupted,
}

public sealed record DeveloperTerminalSessionView(
    DeveloperTerminalSessionId Id,
    WorkspaceId WorkspaceId,
    WorkbenchWorkspaceContext SourceContext,
    DeveloperTerminalWorkingDirectory WorkingDirectory,
    DeveloperTerminalShellName Shell,
    DeveloperTerminalEnvironmentProfile EnvironmentProfile,
    DeveloperTerminalContentPolicy ContentPolicy,
    DeveloperTerminalDimensions Dimensions,
    DeveloperTerminalSessionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    bool IsTrusted,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperTerminalStartRequest(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperTerminalDimensions Dimensions);

public sealed record DeveloperTerminalStartResult(
    DeveloperTerminalSessionView? Session,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperTerminalSessionResult(
    DeveloperTerminalSessionView? Session,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperTerminalListResult(
    IReadOnlyList<DeveloperTerminalSessionView> Sessions);

public sealed record DeveloperTerminalReadResult(
    DeveloperTerminalData Data,
    bool EndOfStream,
    string? ErrorCode,
    string? Error);

public interface IDeveloperTerminalService
{
    ValueTask<DeveloperTerminalStartResult> StartAsync(
        DeveloperTerminalStartRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperTerminalListResult> ListAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperTerminalSessionResult> GetAsync(
        DeveloperTerminalSessionId sessionId,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperTerminalReadResult> ReadAsync(
        DeveloperTerminalSessionId sessionId,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperTerminalSessionResult> WriteAsync(
        DeveloperTerminalSessionId sessionId,
        DeveloperTerminalData data,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperTerminalSessionResult> ResizeAsync(
        DeveloperTerminalSessionId sessionId,
        DeveloperTerminalDimensions dimensions,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperTerminalSessionResult> StopAsync(
        DeveloperTerminalSessionId sessionId,
        CancellationToken cancellationToken = default);
}
