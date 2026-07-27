using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Execution;

internal sealed class DotNetToolRunner : IDotNetToolRunner
{
    private const int MaximumOutputCharacters = 64 * 1024;
    private readonly string executable;

    public DotNetToolRunner() : this("dotnet")
    {
    }

    internal DotNetToolRunner(string executable)
    {
        this.executable = executable;
    }

    public async ValueTask<DotNetToolResult> RunAsync(
        string worktreeRoot,
        DotNetToolRequest request,
        CancellationToken cancellationToken = default)
    {
        string operation = request.Operation.Trim();
        if (!operation.Equals("Build", StringComparison.Ordinal) &&
            !operation.Equals("Test", StringComparison.Ordinal))
        {
            return Failure(request, "invalid_operation", "The operation must be Build or Test.");
        }

        if (!WorkspacePathPolicy.TryResolve(
                worktreeRoot,
                request.EntryPoint,
                out string canonicalRoot,
                out string confinedEntryPoint,
                out string targetEntryPoint,
                out string? errorCode,
                out string? error))
        {
            return Failure(request with { EntryPoint = confinedEntryPoint }, errorCode!, error!);
        }

        if (!File.Exists(targetEntryPoint))
        {
            return Failure(
                request with { EntryPoint = confinedEntryPoint },
                "entry_point_missing",
                "The configured .NET entry point does not exist in the goal worktree.");
        }

        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory = canonicalRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(operation.ToLowerInvariant());
        startInfo.ArgumentList.Add(targetEntryPoint);
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity");
        startInfo.ArgumentList.Add("minimal");
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using Process process = new() { StartInfo = startInfo };
        Stopwatch duration = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                return Failure(
                    request with { Operation = operation, EntryPoint = confinedEntryPoint },
                    "process_start_failed",
                    "The dotnet process did not start.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return Failure(
                request with { Operation = operation, EntryPoint = confinedEntryPoint },
                "process_start_failed",
                exception.Message);
        }

        Task<BoundedText> output = ReadBoundedAsync(process.StandardOutput);
        Task<BoundedText> diagnostic = ReadBoundedAsync(process.StandardError);
        bool wasCancelled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            wasCancelled = true;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
        }

        BoundedText standardOutput = await output;
        BoundedText standardError = await diagnostic;
        duration.Stop();
        return new(
            operation,
            confinedEntryPoint,
            process.ExitCode,
            standardOutput.Value,
            standardError.Value,
            standardOutput.IsTruncated,
            standardError.IsTruncated,
            wasCancelled,
            duration.ElapsedMilliseconds,
            wasCancelled ? "cancelled" : null,
            wasCancelled ? "The dotnet operation was cancelled." : null);
    }

    private static async Task<BoundedText> ReadBoundedAsync(StreamReader reader)
    {
        char[] buffer = new char[4096];
        StringBuilder kept = new(MaximumOutputCharacters);
        bool isTruncated = false;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (read == 0)
            {
                break;
            }

            int remaining = MaximumOutputCharacters - kept.Length;
            if (remaining > 0)
            {
                kept.Append(buffer, 0, Math.Min(read, remaining));
            }

            isTruncated |= read > remaining;
        }

        return new(kept.ToString().Trim(), isTruncated);
    }

    private static DotNetToolResult Failure(
        DotNetToolRequest request,
        string code,
        string error) =>
        new(
            request.Operation,
            request.EntryPoint,
            null,
            string.Empty,
            string.Empty,
            IsOutputTruncated: false,
            IsErrorTruncated: false,
            WasCancelled: false,
            DurationMilliseconds: 0,
            code,
            error);

    private sealed record BoundedText(string Value, bool IsTruncated);
}
