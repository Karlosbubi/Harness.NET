using System.Collections.Immutable;
using System.Diagnostics;
using Porta.Pty;

namespace Harness.DataAccess.Terminal;

internal sealed class PortaDeveloperTerminalConnectionFactory : IDeveloperTerminalConnectionFactory
{
    private const int MinimumColumns = 20;
    private const int MaximumColumns = 400;
    private const int MinimumRows = 5;
    private const int MaximumRows = 200;

    public ValueTask<StoredTerminalShell> ResolveDefaultShellAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? configured = Environment.GetEnvironmentVariable(
            OperatingSystem.IsWindows() ? "ComSpec" : "SHELL");
        string[] candidates = OperatingSystem.IsWindows()
            ? [configured ?? string.Empty, Path.Combine(Environment.SystemDirectory, "cmd.exe")]
            : [configured ?? string.Empty, "/bin/bash", "/bin/zsh", "/bin/sh"];

        string? executable = candidates.FirstOrDefault(IsUsableExecutable);
        if (executable is null)
        {
            throw new InvalidOperationException(
                "No supported interactive shell executable is available.");
        }

        return ValueTask.FromResult(new StoredTerminalShell(
            new(executable),
            new(Path.GetFileName(executable)),
            ImmutableArray<StoredTerminalArgument>.Empty));
    }

    public async ValueTask<IDeveloperTerminalConnection> StartAsync(
        StoredTerminalStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        PtyOptions options = new()
        {
            Name = $"Harness.NET {request.SessionId.Value}",
            App = request.Shell.Executable.Value,
            CommandLine = request.Shell.Arguments.Select(argument => argument.Value).ToArray(),
            Cwd = request.WorkingDirectory.Value,
            Cols = request.Dimensions.Columns,
            Rows = request.Dimensions.Rows,
            Environment = request.Environment.ToDictionary(
                item => item.Name.Value,
                item => item.Value.Value,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            UseAsyncIo = false,
        };

        IPtyConnection connection = await PtyProvider.SpawnAsync(options, cancellationToken);
        return new PortaDeveloperTerminalConnection(connection);
    }

    private static bool IsUsableExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            !File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherExecute)) != 0;
    }

    private static void Validate(StoredTerminalStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId.Value) ||
            request.SessionId.Value.Length > 80)
        {
            throw new ArgumentException("A bounded terminal session identity is required.",
                nameof(request));
        }

        if (!IsUsableExecutable(request.Shell.Executable.Value))
        {
            throw new ArgumentException("The resolved shell executable is unavailable.",
                nameof(request));
        }

        if (!Path.IsPathFullyQualified(request.WorkingDirectory.Value) ||
            !Directory.Exists(request.WorkingDirectory.Value))
        {
            throw new ArgumentException("The terminal working directory is unavailable.",
                nameof(request));
        }

        ValidateDimensions(request.Dimensions);
        if (request.Shell.Arguments.Length > 16 || request.Environment.Length > 16 ||
            request.Environment.Any(entry =>
                string.IsNullOrWhiteSpace(entry.Name.Value) || entry.Name.Value.Length > 128 ||
                entry.Name.Value.Contains('=') || entry.Value.Value.Length > 4_096))
        {
            throw new ArgumentException("The terminal launch policy exceeds its bounds.",
                nameof(request));
        }
    }

    internal static void ValidateDimensions(StoredTerminalDimensions dimensions)
    {
        if (dimensions.Columns is < MinimumColumns or > MaximumColumns ||
            dimensions.Rows is < MinimumRows or > MaximumRows)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions),
                $"Terminal dimensions must be {MinimumColumns}-{MaximumColumns} columns and " +
                $"{MinimumRows}-{MaximumRows} rows.");
        }
    }
}

internal sealed class PortaDeveloperTerminalConnection : IDeveloperTerminalConnection
{
    private const int MaximumReadBytes = 64 * 1024;
    private const int MaximumWriteBytes = 64 * 1024;
    private readonly SemaphoreSlim writerGate = new(1, 1);
    private readonly TaskCompletionSource<StoredTerminalExit> exited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IPtyConnection connection;
    private int stopStarted;
    private int disposed;

    public PortaDeveloperTerminalConnection(IPtyConnection connection)
    {
        this.connection = connection;
        connection.ProcessExited += OnProcessExited;
        if (connection.WaitForExit(0))
        {
            CompleteFromConnection();
        }
    }

    public async ValueTask<StoredTerminalReadResult> ReadAsync(
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (maximumBytes is <= 0 or > MaximumReadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        byte[] buffer = new byte[maximumBytes];
        int read;
        try
        {
            read = await connection.ReaderStream.ReadAsync(buffer, cancellationToken);
        }
        catch (IOException) when (exited.Task.IsCompleted)
        {
            read = 0;
        }

        if (read == 0)
        {
            CompleteFromConnection();
            return new(new(ReadOnlyMemory<byte>.Empty), true);
        }

        return new(new(buffer.AsMemory(0, read)), false);
    }

    public async ValueTask WriteAsync(
        StoredTerminalData data,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (data.Value.IsEmpty || data.Value.Length > MaximumWriteBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        await writerGate.WaitAsync(cancellationToken);
        try
        {
            await connection.WriterStream.WriteAsync(data.Value, cancellationToken);
            await connection.WriterStream.FlushAsync(cancellationToken);
        }
        finally
        {
            writerGate.Release();
        }
    }

    public ValueTask ResizeAsync(
        StoredTerminalDimensions dimensions,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        PortaDeveloperTerminalConnectionFactory.ValidateDimensions(dimensions);
        connection.Resize(dimensions.Columns, dimensions.Rows);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref stopStarted, 1) != 0)
        {
            return;
        }

        bool processTreeKillRequested = TryKillProcessTree(connection.Pid);
        try
        {
            connection.Kill();
        }
        catch (InvalidOperationException) when (processTreeKillRequested || connection.WaitForExit(0))
        {
        }

        await Task.Run(() => connection.WaitForExit(2_000), CancellationToken.None);
        CompleteFromConnection();
    }

    public async ValueTask<StoredTerminalExit> WaitForExitAsync(
        CancellationToken cancellationToken = default)
    {
        if (connection.WaitForExit(0))
        {
            CompleteFromConnection();
        }

        return await exited.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None);
        }
        finally
        {
            connection.ProcessExited -= OnProcessExited;
            connection.Dispose();
            writerGate.Dispose();
            CompleteFromConnection();
        }
    }

    private void OnProcessExited(object? sender, PtyExitedEventArgs args) =>
        exited.TrySetResult(new(args.ExitCode));

    private void CompleteFromConnection() =>
        exited.TrySetResult(new(connection.ExitCode));

    private static bool TryKillProcessTree(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return true;
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

}
