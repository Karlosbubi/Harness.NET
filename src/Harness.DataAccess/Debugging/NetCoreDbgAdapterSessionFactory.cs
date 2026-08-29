using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Debugging;

internal sealed class NetCoreDbgAdapterSessionFactory : IDebugAdapterSessionFactory
{
    private const int MaximumArguments = 128;
    private const int MaximumEnvironmentEntries = 64;
    private readonly IDebugAdapterExecutableResolver executableResolver;
    private readonly IDebugAdapterProcessFactory processFactory;
    private readonly IDotNetDebugProgramResolver dotNetResolver;

    public NetCoreDbgAdapterSessionFactory(IDebugAdapterExecutableResolver executableResolver)
        : this(executableResolver, new DebugAdapterProcessFactory(),
            new DotNetDebugProgramResolver())
    {
    }

    internal NetCoreDbgAdapterSessionFactory(
        IDebugAdapterExecutableResolver executableResolver,
        IDebugAdapterProcessFactory processFactory,
        IDotNetDebugProgramResolver? dotNetResolver = null)
    {
        this.executableResolver = executableResolver;
        this.processFactory = processFactory;
        this.dotNetResolver = dotNetResolver ?? new DotNetDebugProgramResolver();
    }

    public async ValueTask<IDebugAdapterSession> StartAsync(
        StoredDebugAdapterStartRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        string executable = await executableResolver.ResolveVerifiedExecutableAsync(
            cancellationToken) ?? throw new DebugAdapterRequestException(
                "Install or repair the verified managed debugger in Settings first.");
        IDebugAdapterProcess process = processFactory.Start(
            executable, request.WorkingDirectory.Value);
        NetCoreDbgAdapterSession session = new(request.SessionId, request.SourceRoot, process);
        try
        {
            string? program = request.Kind is StoredDebugAdapterStartKind.Launch
                ? dotNetResolver.Resolve()
                : null;
            await session.InitializeAsync(request, program, cancellationToken);
            return session;
        }
        catch
        {
            try { await session.DisposeAsync(); }
            catch (Exception cleanupException) when (cleanupException is IOException or
                                                     OperationCanceledException or
                                                     ObjectDisposedException or
                                                     DebugAdapterRequestException) { }
            throw;
        }
    }

    private static void Validate(StoredDebugAdapterStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind) || !IsIdentifier(request.SessionId.Value))
            throw new DebugAdapterRequestException("The debug session identity is invalid.");
        string workingDirectory = request.WorkingDirectory.Value;
        if (!Path.IsPathFullyQualified(request.SourceRoot.Value) ||
            !Directory.Exists(request.SourceRoot.Value) || IsLink(request.SourceRoot.Value) ||
            !Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory) ||
            IsLink(workingDirectory))
        {
            throw new DebugAdapterRequestException(
                "The debug working directory must be an existing non-symbolic absolute directory.");
        }
        if (request.Arguments.IsDefault || request.Environment.IsDefault ||
            request.Arguments.Length > MaximumArguments ||
            request.Environment.Length > MaximumEnvironmentEntries ||
            request.Arguments.Sum(argument => argument.Value.Length) > 32 * 1024 ||
            request.Environment.Sum(entry => entry.Name.Value.Length + entry.Value.Value.Length) >
            32 * 1024 || request.Arguments.Any(argument =>
                argument.Value.Length > 4 * 1024 || argument.Value.Contains('\0')) ||
            request.Environment.Any(entry => !IsEnvironmentName(entry.Name.Value) ||
                entry.Value.Value.Length > 4 * 1024 || entry.Value.Value.Contains('\0')) ||
            request.Environment.Select(entry => entry.Name.Value)
                .Distinct(StringComparer.Ordinal).Count() != request.Environment.Length)
        {
            throw new DebugAdapterRequestException("The debug launch arguments are invalid.");
        }

        if (request.Kind is StoredDebugAdapterStartKind.Launch)
        {
            if (request.OwnedProcessId is not null)
            {
                throw new DebugAdapterRequestException(
                    "A debugger launch cannot also attach to a process.");
            }
        }
        else if (!request.Arguments.IsEmpty || !request.Environment.IsEmpty ||
                 request.OwnedProcessId is not { Value: > 0 })
        {
            throw new DebugAdapterRequestException(
                "An owned-process attach requires only a positive process identity.");
        }
    }

    private static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 64 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsEnvironmentName(string value) =>
        value.Length is > 0 and <= 128 &&
        (char.IsAsciiLetter(value[0]) || value[0] is '_') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_');

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

internal sealed class NetCoreDbgAdapterSession : IDebugAdapterSession
{
    private const int MaximumItems = 2_000;
    private const int MaximumText = 16 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private readonly IDebugAdapterProcess process;
    private readonly string sourceRoot;
    private readonly DapProtocolStream protocol;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonDocument>> pending = [];
    private readonly Lock pendingLock = new();
    private readonly Channel<StoredDebugEvent> events = Channel.CreateBounded<StoredDebugEvent>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private readonly TaskCompletionSource initialized = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task reader;
    private int sequence;
    private bool disconnected;

    internal NetCoreDbgAdapterSession(
        StoredDebugSessionId id,
        StoredDebugSourceRoot sourceRoot,
        IDebugAdapterProcess process)
    {
        Id = id;
        this.process = process;
        this.sourceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(sourceRoot.Value));
        protocol = new(process.StandardOutput, process.StandardInput);
        reader = ReadLoopAsync();
    }

    public StoredDebugSessionId Id { get; }
    public StoredDebugAdapterCapabilities Capabilities { get; private set; } =
        new(false, false, false, false);

    internal async ValueTask InitializeAsync(
        StoredDebugAdapterStartRequest request,
        string? program,
        CancellationToken cancellationToken)
    {
        using JsonDocument initialize = await RequestAsync("initialize", new
        {
            clientID = "harness.net",
            clientName = "Harness.NET",
            adapterID = "coreclr",
            pathFormat = "path",
            linesStartAt1 = true,
            columnsStartAt1 = true,
            supportsVariableType = true,
            supportsVariablePaging = true,
            supportsRunInTerminalRequest = false,
            locale = "en-us",
        }, cancellationToken);
        JsonElement capabilities = Body(initialize);
        Capabilities = new(
            Boolean(capabilities, "supportsConfigurationDoneRequest"),
            Boolean(capabilities, "supportsConditionalBreakpoints"),
            Boolean(capabilities, "supportsTerminateRequest"),
            SupportsVariablePaging: true);
        if (!Capabilities.SupportsConfigurationDone)
            throw new DebugAdapterRequestException(
                "The managed debugger does not support configuration completion.");

        if (request.Kind is StoredDebugAdapterStartKind.Launch)
        {
            Dictionary<string, string> environment = request.Environment.ToDictionary(
                entry => entry.Name.Value, entry => entry.Value.Value, StringComparer.Ordinal);
            using JsonDocument launch = await RequestAsync("launch", new
            {
                name = "Harness.NET .NET launch",
                type = "coreclr",
                program = program ?? throw new DebugAdapterRequestException(
                    "A resolved .NET SDK executable is required."),
                args = request.Arguments.Select(argument => argument.Value).ToArray(),
                cwd = request.WorkingDirectory.Value,
                env = environment,
                console = "internalConsole",
                stopAtEntry = request.StopAtEntry,
                justMyCode = request.JustMyCode,
                enableStepFiltering = true,
                internalConsoleOptions = "neverOpen",
                __sessionId = request.SessionId.Value,
            }, cancellationToken);
        }
        else
        {
            using JsonDocument attach = await RequestAsync("attach", new
            {
                processId = request.OwnedProcessId!.Value,
            }, cancellationToken);
        }

        await initialized.Task.WaitAsync(RequestTimeout, cancellationToken);
    }

    public IAsyncEnumerable<StoredDebugEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default) =>
        events.Reader.ReadAllAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<StoredDebugBreakpoint>> SetBreakpointsAsync(
        StoredDebugSourcePath source,
        IReadOnlyList<StoredDebugBreakpointRequest> breakpoints,
        CancellationToken cancellationToken = default)
    {
        if (!WorkspacePathPolicy.TryResolve(sourceRoot, source.Value, out _,
                out string confinedSource, out string absoluteSource, out _, out _) ||
            !File.Exists(absoluteSource) ||
            breakpoints.Count > 1_000 || breakpoints.Any(item => item.Source != source ||
                item.Line.Value <= 0 || item.Condition?.Length > 1_024 ||
                item.Condition?.Contains('\0') == true) ||
            breakpoints.Select(item => item.Line.Value).Distinct().Count() != breakpoints.Count)
            throw new DebugAdapterRequestException("The breakpoint request is invalid.");
        using JsonDocument response = await RequestAsync("setBreakpoints", new
        {
            source = new { name = Path.GetFileName(confinedSource), path = absoluteSource },
            lines = breakpoints.Select(item => item.Line.Value).ToArray(),
            breakpoints = breakpoints.Select(item => new
            {
                line = item.Line.Value,
                condition = item.Condition,
            }).ToArray(),
            sourceModified = false,
        }, cancellationToken);
        JsonElement[] returned = Body(response).GetProperty("breakpoints")
            .EnumerateArray().Take(MaximumItems).ToArray();
        if (returned.Length != breakpoints.Count)
            throw new DebugAdapterRequestException(
                "The debug adapter returned an unexpected breakpoint count.");
        List<StoredDebugBreakpoint> mapped = [];
        int index = 0;
        foreach (JsonElement item in returned)
        {
            StoredDebugBreakpointRequest requested = breakpoints[index];
            mapped.Add(new(
                IntegerOrNull(item, "id"),
                Boolean(item, "verified"),
                new(confinedSource.Replace(Path.DirectorySeparatorChar, '/')),
                requested.Line,
                IntegerOrNull(item, "line") is int line ? new(line) : null,
                TextOrNull(item, "message")));
            index++;
        }
        return mapped;
    }

    public async ValueTask CompleteConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await RequestAsync("configurationDone", new { },
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredDebugThread>> GetThreadsAsync(
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await RequestAsync("threads", new { }, cancellationToken);
        return Body(response).GetProperty("threads").EnumerateArray().Take(MaximumItems)
            .Select(item => new StoredDebugThread(
                new(item.GetProperty("id").GetInt32()),
                RequiredText(item, "name"))).ToArray();
    }

    public async ValueTask<IReadOnlyList<StoredDebugStackFrame>> GetStackTraceAsync(
        StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(threadId.Value, "thread");
        using JsonDocument response = await RequestAsync("stackTrace", new
        {
            threadId = threadId.Value,
            startFrame = 0,
            levels = 200,
        }, cancellationToken);
        return Body(response).GetProperty("stackFrames").EnumerateArray().Take(200)
            .Select(item =>
            {
                JsonElement source = item.TryGetProperty("source", out JsonElement found)
                    ? found : default;
                string? path = source.ValueKind is JsonValueKind.Object
                    ? TextOrNull(source, "path") : null;
                string? confinedPath = ConfineAdapterSource(path);
                int? line = IntegerOrNull(item, "line");
                return new StoredDebugStackFrame(
                    new(item.GetProperty("id").GetInt32()),
                    RequiredText(item, "name"),
                    confinedPath is null ? null : new(confinedPath),
                    line is > 0 ? new(line.Value) : null,
                    IntegerOrNull(item, "column"));
            }).ToArray();
    }

    public async ValueTask<IReadOnlyList<StoredDebugScope>> GetScopesAsync(
        StoredDebugStackFrameId frameId,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(frameId.Value, "stack frame");
        using JsonDocument response = await RequestAsync("scopes", new
        {
            frameId = frameId.Value,
        }, cancellationToken);
        return Body(response).GetProperty("scopes").EnumerateArray().Take(100)
            .Select(item => new StoredDebugScope(
                RequiredText(item, "name"),
                new(item.GetProperty("variablesReference").GetInt32()),
                Boolean(item, "expensive"))).ToArray();
    }

    public async ValueTask<IReadOnlyList<StoredDebugVariable>> GetVariablesAsync(
        StoredDebugVariablesReference variablesReference,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(variablesReference.Value, "variables reference");
        using JsonDocument response = await RequestAsync("variables", new
        {
            variablesReference = variablesReference.Value,
            start = 0,
            count = MaximumItems,
        }, cancellationToken);
        return Body(response).GetProperty("variables").EnumerateArray().Take(MaximumItems)
            .Select(item => new StoredDebugVariable(
                new(RequiredText(item, "name")),
                new(RequiredText(item, "value")),
                TextOrNull(item, "type") is { } type ? new(type) : null,
                new(item.GetProperty("variablesReference").GetInt32()),
                IntegerOrNull(item, "namedVariables"),
                IntegerOrNull(item, "indexedVariables"))).ToArray();
    }

    public ValueTask ContinueAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        ThreadCommandAsync("continue", threadId, cancellationToken);

    public ValueTask PauseAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        ThreadCommandAsync("pause", threadId, cancellationToken);

    public ValueTask StepOverAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        ThreadCommandAsync("next", threadId, cancellationToken);

    public ValueTask StepInAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        ThreadCommandAsync("stepIn", threadId, cancellationToken);

    public ValueTask StepOutAsync(StoredDebugThreadId threadId,
        CancellationToken cancellationToken = default) =>
        ThreadCommandAsync("stepOut", threadId, cancellationToken);

    public async ValueTask DisconnectAsync(
        bool terminateDebuggee,
        CancellationToken cancellationToken = default)
    {
        if (disconnected) return;
        disconnected = true;
        try
        {
            using JsonDocument response = await RequestAsync("disconnect", new
            {
                restart = false,
                terminateDebuggee,
            }, cancellationToken);
        }
        finally
        {
            if (terminateDebuggee && !process.HasExited) process.Kill();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!disconnected)
        {
            try
            {
                await DisconnectAsync(terminateDebuggee: true, CancellationToken.None);
            }
            catch (Exception exception) when (exception is DebugAdapterRequestException or
                                              OperationCanceledException or IOException or
                                              ObjectDisposedException)
            {
                if (!process.HasExited) process.Kill();
            }
        }
        lifetime.Cancel();
        try { await reader; }
        catch (OperationCanceledException) { }
        await process.DisposeAsync();
        lifetime.Dispose();
    }

    private async ValueTask ThreadCommandAsync(
        string command,
        StoredDebugThreadId threadId,
        CancellationToken cancellationToken)
    {
        EnsurePositive(threadId.Value, "thread");
        using JsonDocument response = await RequestAsync(command, new
        {
            threadId = threadId.Value,
        }, cancellationToken);
    }

    private async ValueTask<JsonDocument> RequestAsync<T>(
        string command,
        T arguments,
        CancellationToken cancellationToken)
    {
        int requestSequence = Interlocked.Increment(ref sequence);
        TaskCompletionSource<JsonDocument> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (pendingLock) pending.Add(requestSequence, completion);
        try
        {
            await protocol.WriteAsync(new
            {
                seq = requestSequence,
                type = "request",
                command,
                arguments,
            }, cancellationToken);
            JsonDocument response = await completion.Task.WaitAsync(RequestTimeout,
                cancellationToken);
            if (!Boolean(response.RootElement, "success"))
            {
                string message = TextOrNull(response.RootElement, "message") ??
                    $"The debug adapter rejected {command}.";
                response.Dispose();
                throw new DebugAdapterRequestException(Limit(message)!);
            }
            return response;
        }
        catch
        {
            lock (pendingLock) pending.Remove(requestSequence);
            throw;
        }
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                JsonDocument? message = await protocol.ReadAsync(lifetime.Token);
                if (message is null) break;
                JsonElement root = message.RootElement;
                string? type = TextOrNull(root, "type");
                if (type == "response" && IntegerOrNull(root, "request_seq") is int request)
                {
                    TaskCompletionSource<JsonDocument>? completion;
                    lock (pendingLock)
                    {
                        pending.Remove(request, out completion);
                    }
                    if (completion is null) message.Dispose();
                    else completion.TrySetResult(message);
                }
                else if (type == "event")
                {
                    StoredDebugEvent? mapped = MapEvent(root);
                    message.Dispose();
                    if (mapped is not null)
                    {
                        if (mapped.Kind is StoredDebugEventKind.Initialized)
                            initialized.TrySetResult();
                        PublishEvent(mapped);
                    }
                }
                else
                {
                    message.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            failure = exception;
            PublishEvent(new(StoredDebugEventKind.AdapterFailed, StoredDebugStopReason.None,
                null, "The debug adapter connection closed unexpectedly.", process.ExitCode,
                AllThreadsStopped: false));
        }
        finally
        {
            DebugAdapterRequestException closed = new(failure is null
                ? "The debug adapter connection closed."
                : "The debug adapter connection failed.");
            lock (pendingLock)
            {
                foreach (TaskCompletionSource<JsonDocument> completion in pending.Values)
                    completion.TrySetException(closed);
                pending.Clear();
            }
            initialized.TrySetException(closed);
            events.Writer.TryComplete();
        }
    }

    private void PublishEvent(StoredDebugEvent value)
    {
        events.Writer.TryWrite(value);
    }

    private static StoredDebugEvent? MapEvent(JsonElement root)
    {
        string? name = TextOrNull(root, "event");
        JsonElement body = root.TryGetProperty("body", out JsonElement found) ? found : default;
        return name switch
        {
            "initialized" => new(StoredDebugEventKind.Initialized, StoredDebugStopReason.None,
                null, null, null, false),
            "stopped" => new(StoredDebugEventKind.Stopped,
                StopReason(TextOrNull(body, "reason")),
                IntegerOrNull(body, "threadId") is int stoppedThread ? new(stoppedThread) : null,
                TextOrNull(body, "description"), null,
                Boolean(body, "allThreadsStopped")),
            "continued" => new(StoredDebugEventKind.Continued, StoredDebugStopReason.None,
                IntegerOrNull(body, "threadId") is int continuedThread ? new(continuedThread) : null,
                null, null, false),
            "output" => new(StoredDebugEventKind.Output, StoredDebugStopReason.None,
                null, Limit(TextOrNull(body, "output")), null, false),
            "exited" => new(StoredDebugEventKind.Exited, StoredDebugStopReason.None,
                null, null, IntegerOrNull(body, "exitCode"), false),
            "terminated" => new(StoredDebugEventKind.Terminated, StoredDebugStopReason.None,
                null, null, null, false),
            "thread" => new(StoredDebugEventKind.ThreadChanged, StoredDebugStopReason.None,
                IntegerOrNull(body, "threadId") is int changedThread ? new(changedThread) : null,
                TextOrNull(body, "reason"), null, false),
            "breakpoint" => new(StoredDebugEventKind.BreakpointChanged,
                StoredDebugStopReason.None, null, TextOrNull(body, "reason"), null, false),
            _ => null,
        };
    }

    private static StoredDebugStopReason StopReason(string? reason) => reason switch
    {
        "entry" => StoredDebugStopReason.Entry,
        "breakpoint" => StoredDebugStopReason.Breakpoint,
        "step" => StoredDebugStopReason.Step,
        "pause" => StoredDebugStopReason.Pause,
        "exception" => StoredDebugStopReason.Exception,
        null or "" => StoredDebugStopReason.None,
        _ => StoredDebugStopReason.Unknown,
    };

    private static JsonElement Body(JsonDocument response) =>
        response.RootElement.TryGetProperty("body", out JsonElement body) &&
        body.ValueKind is JsonValueKind.Object
            ? body
            : throw new DebugAdapterRequestException(
                "The debug adapter response did not contain a valid body.");

    private static bool Boolean(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object &&
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True;

    private static int? IntegerOrNull(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object &&
        element.TryGetProperty(name, out JsonElement value) &&
        value.TryGetInt32(out int parsed) ? parsed : null;

    private static string RequiredText(JsonElement element, string name) =>
        TextOrNull(element, name) ?? throw new DebugAdapterRequestException(
            $"The debug adapter omitted {name}.");

    private static string? TextOrNull(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object &&
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind is JsonValueKind.String
            ? Limit(value.GetString())
            : null;

    private static string? Limit(string? value) => value is null
        ? null
        : value.Length <= MaximumText ? value : value[..MaximumText];

    private static void EnsurePositive(int value, string name)
    {
        if (value <= 0)
            throw new DebugAdapterRequestException($"A positive {name} identity is required.");
    }

    private string? ConfineAdapterSource(string? path)
    {
        if (path is null || !Path.IsPathFullyQualified(path)) return null;
        string relative;
        try { relative = Path.GetRelativePath(sourceRoot, Path.GetFullPath(path)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            return null;
        }
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
