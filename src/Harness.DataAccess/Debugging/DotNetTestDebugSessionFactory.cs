using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Debugging;

internal sealed class DotNetTestDebugSessionFactory : IDotNetTestDebugSessionFactory
{
    private readonly IDebugAdapterSessionFactory adapterFactory;
    private readonly IDotNetDebugProgramResolver dotNetResolver;
    private readonly IOwnedDotNetTestProcessFactory processFactory;

    public DotNetTestDebugSessionFactory(IDebugAdapterSessionFactory adapterFactory)
        : this(adapterFactory, new DotNetDebugProgramResolver(),
            new OwnedDotNetTestProcessFactory())
    {
    }

    internal DotNetTestDebugSessionFactory(
        IDebugAdapterSessionFactory adapterFactory,
        IDotNetDebugProgramResolver dotNetResolver,
        IOwnedDotNetTestProcessFactory processFactory)
    {
        this.adapterFactory = adapterFactory;
        this.dotNetResolver = dotNetResolver;
        this.processFactory = processFactory;
    }

    public async ValueTask<IDebugAdapterSession> StartAsync(
        string sourceRoot,
        StoredDotNetTestDebugRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
            throw new DebugAdapterRequestException(
                "Owned Test Debug process discovery is currently supported on Linux.");
        ArgumentNullException.ThrowIfNull(request);
        if (!WorkspacePathPolicy.TryResolve(sourceRoot, request.ProjectPath.Value,
                out string canonicalRoot, out _, out string projectPath, out _, out string? error))
            throw new DebugAdapterRequestException(error ?? "The test project is outside the source context.");
        FileInfo project = new(projectPath);
        if (!project.Exists || project.LinkTarget is not null ||
            !project.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            !IsTestName(request.Test.Value) ||
            request.TargetFramework is { Value: { } framework } && !IsFramework(framework) ||
            request.Configuration is { Value: { } configuration } && !IsConfiguration(configuration))
        {
            throw new DebugAdapterRequestException("The exact Test Debug target is invalid.");
        }

        List<string> arguments =
        [
            "test", projectPath, "--no-restore", "--filter",
            $"FullyQualifiedName={request.Test.Value}",
            "--logger", "console;verbosity=minimal",
        ];
        if (request.TargetFramework is { Value: not "unknown" } target)
        {
            arguments.Add("--framework");
            arguments.Add(target.Value);
        }
        if (request.Configuration is { } selected)
        {
            arguments.Add("--configuration");
            arguments.Add(selected.Value);
        }

        IOwnedDotNetTestProcess? process = null;
        IDebugAdapterSession? adapter = null;
        try
        {
            process = processFactory.Start(
                dotNetResolver.Resolve(), canonicalRoot, arguments);
            StoredDebugProcessId testHost = await process.WaitForTestHostAsync(cancellationToken);
            if (!process.IsLiveDescendant(testHost))
                throw new DebugAdapterRequestException(
                    "The owned testhost ancestry changed before debugger attach.");
            adapter = await adapterFactory.StartAsync(new(
                request.SessionId,
                StoredDebugAdapterStartKind.AttachOwnedProcess,
                new(canonicalRoot),
                new(canonicalRoot),
                ImmutableArray<StoredDebugArgument>.Empty,
                ImmutableArray<StoredDebugEnvironmentEntry>.Empty,
                testHost,
                StopAtEntry: false,
                request.JustMyCode,
                process.RootProcessId), cancellationToken);
            return new OwnedTestDebugAdapterSession(adapter, process);
        }
        catch
        {
            if (adapter is not null) await SafeDisposeAsync(adapter);
            if (process is not null) await process.DisposeAsync();
            throw;
        }
    }

    private static bool IsTestName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 512 &&
        value.Equals(value.Trim(), StringComparison.Ordinal) &&
        value.All(character => char.IsLetterOrDigit(character) ||
            character is '.' or '_' or '+' or '`');

    private static bool IsFramework(string value) =>
        value.Length is > 0 and <= 128 && value.Equals(value.Trim(), StringComparison.Ordinal) &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-');

    private static bool IsConfiguration(string value) =>
        value.Length is > 0 and <= 128 && value.Equals(value.Trim(), StringComparison.Ordinal) &&
        value.All(character => !char.IsControl(character));

    private static async ValueTask SafeDisposeAsync(IDebugAdapterSession adapter)
    {
        try { await adapter.DisposeAsync(); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }
    }
}

internal sealed class OwnedTestDebugAdapterSession(
    IDebugAdapterSession adapter,
    IOwnedDotNetTestProcess process) : IDebugAdapterSession
{
    private bool disconnected;
    private bool disposed;

    public StoredDebugSessionId Id => adapter.Id;
    public StoredDebugAdapterCapabilities Capabilities => adapter.Capabilities;

    public async IAsyncEnumerable<StoredDebugEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (StoredDebugEvent value in adapter.ReadEventsAsync(cancellationToken))
        {
            if (value.Kind is StoredDebugEventKind.Exited) continue;
            if (value.Kind is not StoredDebugEventKind.Terminated)
            {
                yield return value;
                continue;
            }
            OwnedDotNetTestResult result;
            using (CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken, timeout.Token))
            {
                try
                {
                    result = await process.WaitForExitAsync(linked.Token);
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    process.Kill();
                    result = await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            if (result.Output.Length > 0)
                yield return new(StoredDebugEventKind.Output, StoredDebugStopReason.None,
                    null, result.Output, null, false);
            yield return new(StoredDebugEventKind.Exited, StoredDebugStopReason.None,
                null, null, result.ExitCode, false);
            yield return value;
        }
    }

    public ValueTask<IReadOnlyList<StoredDebugBreakpoint>> SetBreakpointsAsync(
        StoredDebugSourcePath source,
        IReadOnlyList<StoredDebugBreakpointRequest> breakpoints,
        CancellationToken cancellationToken = default) =>
        adapter.SetBreakpointsAsync(source, breakpoints, cancellationToken);

    public ValueTask CompleteConfigurationAsync(CancellationToken cancellationToken = default) =>
        adapter.CompleteConfigurationAsync(cancellationToken);

    public ValueTask<IReadOnlyList<StoredDebugThread>> GetThreadsAsync(
        CancellationToken cancellationToken = default) => adapter.GetThreadsAsync(cancellationToken);

    public ValueTask<IReadOnlyList<StoredDebugStackFrame>> GetStackTraceAsync(
        StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        adapter.GetStackTraceAsync(threadId, cancellationToken);

    public ValueTask<IReadOnlyList<StoredDebugScope>> GetScopesAsync(
        StoredDebugStackFrameId frameId,
        CancellationToken cancellationToken = default) =>
        adapter.GetScopesAsync(frameId, cancellationToken);

    public ValueTask<IReadOnlyList<StoredDebugVariable>> GetVariablesAsync(
        StoredDebugVariablesReference variablesReference,
        CancellationToken cancellationToken = default) =>
        adapter.GetVariablesAsync(variablesReference, cancellationToken);

    public ValueTask ContinueAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        adapter.ContinueAsync(threadId, cancellationToken);

    public ValueTask PauseAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        adapter.PauseAsync(threadId, cancellationToken);

    public ValueTask StepOverAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        adapter.StepOverAsync(threadId, cancellationToken);

    public ValueTask StepInAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        adapter.StepInAsync(threadId, cancellationToken);

    public ValueTask StepOutAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        adapter.StepOutAsync(threadId, cancellationToken);

    public async ValueTask DisconnectAsync(
        bool terminateDebuggee,
        CancellationToken cancellationToken = default)
    {
        if (disconnected) return;
        disconnected = true;
        try { await adapter.DisconnectAsync(terminateDebuggee, cancellationToken); }
        finally { if (terminateDebuggee) process.Kill(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { await adapter.DisposeAsync(); }
        finally { await process.DisposeAsync(); }
    }
}

internal sealed record OwnedDotNetTestResult(int ExitCode, string Output);

internal interface IOwnedDotNetTestProcess : IAsyncDisposable
{
    StoredDebugProcessId RootProcessId { get; }
    ValueTask<StoredDebugProcessId> WaitForTestHostAsync(CancellationToken cancellationToken);
    bool IsLiveDescendant(StoredDebugProcessId processId);
    ValueTask<OwnedDotNetTestResult> WaitForExitAsync(CancellationToken cancellationToken);
    void Kill();
}

internal interface IOwnedDotNetTestProcessFactory
{
    IOwnedDotNetTestProcess Start(
        string dotNetExecutable,
        string workingDirectory,
        IReadOnlyList<string> arguments);
}

internal sealed class OwnedDotNetTestProcessFactory : IOwnedDotNetTestProcessFactory
{
    public IOwnedDotNetTestProcess Start(
        string dotNetExecutable,
        string workingDirectory,
        IReadOnlyList<string> arguments) =>
        OwnedDotNetTestProcess.Start(dotNetExecutable, workingDirectory, arguments);
}

internal sealed class OwnedDotNetTestProcess : IOwnedDotNetTestProcess
{
    private const int MaximumOutputCharacters = 256 * 1024;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(90);
    private readonly Process process;
    private readonly Task<string> output;
    private readonly Task<string> diagnostic;

    private OwnedDotNetTestProcess(Process process)
    {
        this.process = process;
        output = ReadBoundedAsync(process.StandardOutput);
        diagnostic = ReadBoundedAsync(process.StandardError);
    }

    public StoredDebugProcessId RootProcessId => new(process.Id);

    internal static OwnedDotNetTestProcess Start(
        string dotNetExecutable,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        ProcessStartInfo start = new(dotNetExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["VSTEST_HOST_DEBUG"] = "1";
        Process process = new() { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new DebugAdapterRequestException("The owned Test Debug process did not start.");
            return new(process);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            process.Dispose();
            throw new DebugAdapterRequestException(
                $"The owned Test Debug process did not start: {exception.Message}");
        }
    }

    public async ValueTask<StoredDebugProcessId> WaitForTestHostAsync(
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(DiscoveryTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);
        try
        {
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new DebugAdapterRequestException(
                        "The exact test operation exited before its testhost was ready.");
                int[] matches = LinuxProcessTree.Descendants(process.Id)
                    .Where(LinuxProcessTree.IsManagedTestHost)
                    .Take(2)
                    .ToArray();
                if (matches.Length == 1) return new(matches[0]);
                if (matches.Length > 1)
                    throw new DebugAdapterRequestException(
                        "The exact test operation created multiple candidate testhosts.");
                await Task.Delay(TimeSpan.FromMilliseconds(100), linked.Token);
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new DebugAdapterRequestException(
                "The exact test operation did not create a waiting testhost in time.");
        }
    }

    public bool IsLiveDescendant(StoredDebugProcessId processId) =>
        LinuxProcessTree.IsDescendant(process.Id, processId.Value) &&
        LinuxProcessTree.IsManagedTestHost(processId.Value);

    public async ValueTask<OwnedDotNetTestResult> WaitForExitAsync(
        CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken);
        string standardOutput = await output;
        string standardError = await diagnostic;
        string combined = string.Join(Environment.NewLine,
            new[] { standardOutput, standardError }.Where(value => value.Length > 0));
        if (combined.Length > MaximumOutputCharacters)
            combined = combined[..MaximumOutputCharacters];
        return new(process.ExitCode, combined);
    }

    public void Kill()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        await Task.WhenAll(output, diagnostic);
        process.Dispose();
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        char[] buffer = new char[4096];
        StringBuilder kept = new(MaximumOutputCharacters);
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (read == 0) return kept.ToString().TrimEnd();
            int remaining = MaximumOutputCharacters - kept.Length;
            if (remaining > 0) kept.Append(buffer, 0, Math.Min(read, remaining));
        }
    }
}

internal static class LinuxProcessTree
{
    private const int MaximumProcesses = 256;
    private const int MaximumCommandBytes = 64 * 1024;

    internal static IEnumerable<int> Descendants(int root)
    {
        Queue<int> pending = new();
        HashSet<int> seen = [];
        pending.Enqueue(root);
        while (pending.Count > 0 && seen.Count < MaximumProcesses)
        {
            int parent = pending.Dequeue();
            foreach (int child in Children(parent))
            {
                if (!seen.Add(child)) continue;
                yield return child;
                pending.Enqueue(child);
            }
        }
    }

    internal static bool IsDescendant(int root, int candidate)
    {
        int current = candidate;
        for (int depth = 0; depth < 64 && current > 1; depth++)
        {
            int? parent = Parent(current);
            if (parent == root) return true;
            if (parent is null || parent == current) return false;
            current = parent.Value;
        }
        return false;
    }

    internal static bool IsManagedTestHost(int processId)
    {
        byte[]? bytes = ReadBounded($"/proc/{processId}/cmdline");
        if (bytes is null) return false;
        return bytes.Split((byte)0).Any(argument =>
            Path.GetFileName(Encoding.UTF8.GetString(argument.Span))
                .Equals("testhost.dll", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<int> Children(int parent)
    {
        string[] taskDirectories;
        try
        {
            taskDirectories = Directory.EnumerateDirectories($"/proc/{parent}/task")
                .Take(64).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        HashSet<int> emitted = [];
        foreach (string taskDirectory in taskDirectories)
        {
            string value;
            try { value = File.ReadAllText(Path.Combine(taskDirectory, "children")); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string item in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(item, out int parsed) && parsed > 1 && emitted.Add(parsed))
                    yield return parsed;
        }
    }

    private static int? Parent(int processId)
    {
        try
        {
            foreach (string line in File.ReadLines($"/proc/{processId}/status"))
                if (line.StartsWith("PPid:", StringComparison.Ordinal) &&
                    int.TryParse(line[5..].Trim(), out int parent)) return parent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    private static byte[]? ReadBounded(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buffer = new byte[MaximumCommandBytes + 1];
            int length = stream.ReadAtLeast(buffer, 1, throwOnEndOfStream: false);
            while (length <= MaximumCommandBytes)
            {
                int read = stream.Read(buffer, length, buffer.Length - length);
                if (read == 0) return buffer[..length];
                length += read;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }
}

internal static class ByteArraySegments
{
    internal static IEnumerable<ReadOnlyMemory<byte>> Split(this byte[] value, byte separator)
    {
        int start = 0;
        for (int index = 0; index <= value.Length; index++)
        {
            if (index != value.Length && value[index] != separator) continue;
            if (index > start) yield return value.AsMemory(start, index - start);
            start = index + 1;
        }
    }
}
