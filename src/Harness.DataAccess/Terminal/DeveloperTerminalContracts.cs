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
