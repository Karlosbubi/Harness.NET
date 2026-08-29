using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Pipes;
using System.Text.Json;
using Harness.DataAccess.Debugging;

namespace Harness.DataAccess.Tests.Debugging;

public sealed class NetCoreDbgAdapterSessionTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-dap-session-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Runs_typed_launch_inspection_stepping_and_termination_over_stdio()
    {
        Directory.CreateDirectory(root);
        string program = Path.Combine(root, "dotnet");
        await File.WriteAllTextAsync(program, "fixture");
        string source = Path.Combine(root, "Program.cs");
        await File.WriteAllTextAsync(source, "class Program { static void Main() { } }");
        FakeAdapterProcess process = new();
        NetCoreDbgAdapterSessionFactory factory = new(
            new ExecutableResolver(), new ProcessFactory(process), new ProgramResolver(program));
        StoredDebugAdapterStartRequest request = new(
            new("session_1"),
            StoredDebugAdapterStartKind.Launch,
            new(root),
            new(root),
            [new("run"), new("--no-restore")],
            [new(new("DOTNET_NOLOGO"), new("1"))],
            null,
            StopAtEntry: true,
            JustMyCode: true);

        await using IDebugAdapterSession session = await factory.StartAsync(request);
        IReadOnlyList<StoredDebugBreakpoint> breakpoints = await session.SetBreakpointsAsync(
            new("Program.cs"),
            [new(new("Program.cs"), new(12))]);
        await session.CompleteConfigurationAsync();
        StoredDebugEvent stopped = await EventAsync(session, StoredDebugEventKind.Stopped);
        IReadOnlyList<StoredDebugThread> threads = await session.GetThreadsAsync();
        IReadOnlyList<StoredDebugStackFrame> stack = await session.GetStackTraceAsync(
            stopped.ThreadId!);
        IReadOnlyList<StoredDebugScope> scopes = await session.GetScopesAsync(stack[0].Id);
        IReadOnlyList<StoredDebugVariable> variables = await session.GetVariablesAsync(
            scopes[0].VariablesReference);
        await session.StepOverAsync(stopped.ThreadId!);
        StoredDebugEvent stepped = await EventAsync(session, StoredDebugEventKind.Stopped);
        await session.DisconnectAsync(terminateDebuggee: true);

        Assert.True(session.Capabilities.SupportsConfigurationDone);
        Assert.True(session.Capabilities.SupportsConditionalBreakpoints);
        Assert.True(Assert.Single(breakpoints).IsVerified);
        Assert.Equal(12, breakpoints[0].ActualLine?.Value);
        Assert.Equal(StoredDebugStopReason.Breakpoint, stopped.StopReason);
        Assert.Equal("Main Thread", Assert.Single(threads).Name);
        Assert.Equal("Program.Main()", Assert.Single(stack).Name);
        Assert.Equal("Locals", Assert.Single(scopes).Name);
        Assert.Equal("answer", Assert.Single(variables).Name.Value);
        Assert.Equal("42", variables[0].Value.Value);
        Assert.Equal(StoredDebugStopReason.Step, stepped.StopReason);
        Assert.Equal(
            ["initialize", "launch", "setBreakpoints", "configurationDone", "threads",
             "stackTrace", "scopes", "variables", "next", "disconnect"],
            process.Commands.ToArray());
        JsonElement launch = process.Arguments.Single(item => item.Command == "launch").Arguments;
        Assert.Equal(program, launch.GetProperty("program").GetString());
        Assert.Equal("internalConsole", launch.GetProperty("console").GetString());
        Assert.False(launch.TryGetProperty("server", out _));
    }

    [Fact]
    public async Task Rejects_unverified_adapter_or_unowned_attach_before_start()
    {
        Directory.CreateDirectory(root);
        string program = Path.Combine(root, "dotnet");
        await File.WriteAllTextAsync(program, "fixture");
        NetCoreDbgAdapterSessionFactory unavailable = new(
            new ExecutableResolver(available: false), new ProcessFactory(new()),
            new ProgramResolver(program));
        StoredDebugAdapterStartRequest launch = new(
            new("session"), StoredDebugAdapterStartKind.Launch, new(root), new(root),
            [], [], null, false, true);

        await Assert.ThrowsAsync<DebugAdapterRequestException>(async () =>
            await unavailable.StartAsync(launch));

        StoredDebugAdapterStartRequest invalidAttach = launch with
        {
            Kind = StoredDebugAdapterStartKind.AttachOwnedProcess,
            OwnedProcessId = new(0),
        };
        await Assert.ThrowsAsync<DebugAdapterRequestException>(async () =>
            await unavailable.StartAsync(invalidAttach));
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    [Trait("Tier", "Live")]
    public async Task Pinned_netcoredbg_stops_and_inspects_a_real_dotnet_10_process()
    {
        string? adapter = Environment.GetEnvironmentVariable("HARNESS_NETCOREDBG_LIVE_PATH");
        string? target = Environment.GetEnvironmentVariable("HARNESS_DEBUG_TARGET_DLL");
        string? dotnet = Environment.ProcessPath;
        if (adapter is null || target is null || dotnet is null) return;

        string workingDirectory = Path.GetDirectoryName(target)!;
        NetCoreDbgAdapterSessionFactory factory = new(
            new ExecutableResolver(path: adapter), new DebugAdapterProcessFactory(),
            new ProgramResolver(dotnet));
        StoredDebugAdapterStartRequest request = new(
            new("live_netcoredbg"),
            StoredDebugAdapterStartKind.Launch,
            new(workingDirectory),
            new(workingDirectory),
            [new(target), new("--help")],
            [],
            null,
            StopAtEntry: true,
            JustMyCode: true);

        await using IDebugAdapterSession session = await factory.StartAsync(request);
        await session.CompleteConfigurationAsync();
        StoredDebugEvent stopped = await EventAsync(session, StoredDebugEventKind.Stopped);
        IReadOnlyList<StoredDebugThread> threads = await session.GetThreadsAsync();
        IReadOnlyList<StoredDebugStackFrame> stack = await session.GetStackTraceAsync(
            stopped.ThreadId!);
        await session.DisconnectAsync(terminateDebuggee: true);

        Assert.Equal(StoredDebugStopReason.Entry, stopped.StopReason);
        Assert.NotEmpty(threads);
        Assert.NotEmpty(stack);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static async Task<StoredDebugEvent> EventAsync(
        IDebugAdapterSession session,
        StoredDebugEventKind kind)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await foreach (StoredDebugEvent value in session.ReadEventsAsync(timeout.Token))
        {
            if (value.Kind == kind) return value;
        }
        throw new InvalidOperationException($"No {kind} event was received.");
    }

    private sealed class ExecutableResolver(
        bool available = true,
        string path = "/managed/netcoredbg") : IDebugAdapterExecutableResolver
    {
        public ValueTask<string?> ResolveVerifiedExecutableAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(available ? path : null);
    }

    private sealed class ProcessFactory(FakeAdapterProcess process) : IDebugAdapterProcessFactory
    {
        public IDebugAdapterProcess Start(string executable, string workingDirectory)
        {
            Assert.Equal("/managed/netcoredbg", executable);
            Assert.DoesNotContain("--server", executable, StringComparison.Ordinal);
            process.Start();
            return process;
        }
    }

    private sealed class ProgramResolver(string path) : IDotNetDebugProgramResolver
    {
        public string Resolve() => path;
    }

    private sealed class FakeAdapterProcess : IDebugAdapterProcess
    {
        private readonly NamedPipeServerStream appInput;
        private readonly NamedPipeClientStream adapterInput;
        private readonly NamedPipeServerStream adapterOutput;
        private readonly NamedPipeClientStream appOutput;
        private readonly CancellationTokenSource lifetime = new();
        private Task? adapter;
        private bool exited;

        internal FakeAdapterProcess()
        {
            (appInput, adapterInput) = CreatePipePair();
            (adapterOutput, appOutput) = CreatePipePair();
        }

        public Stream StandardInput => appInput;
        public Stream StandardOutput => appOutput;
        public bool HasExited => exited;
        public int? ExitCode => exited ? 0 : null;
        public string Diagnostic => string.Empty;
        internal ConcurrentQueue<string> Commands { get; } = new();
        internal ConcurrentQueue<(string Command, JsonElement Arguments)> Arguments { get; } = new();

        internal void Start() => adapter = RunAdapterAsync();

        public void Kill()
        {
            exited = true;
            lifetime.Cancel();
            adapterOutput.Dispose();
        }

        public async ValueTask WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            if (adapter is not null) await adapter.WaitAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            appInput.Dispose();
            appOutput.Dispose();
            adapterInput.Dispose();
            adapterOutput.Dispose();
            if (adapter is not null)
            {
                try { await adapter; }
                catch (Exception exception) when (exception is IOException or
                                                  OperationCanceledException or
                                                  ObjectDisposedException) { }
            }
            lifetime.Dispose();
        }

        private async Task RunAdapterAsync()
        {
            DapProtocolStream protocol = new(adapterInput, adapterOutput);
            int sequence = 100;
            while (!lifetime.IsCancellationRequested)
            {
                using JsonDocument? request = await protocol.ReadAsync(lifetime.Token);
                if (request is null) return;
                JsonElement root = request.RootElement;
                string command = root.GetProperty("command").GetString()!;
                int requestSequence = root.GetProperty("seq").GetInt32();
                JsonElement arguments = root.GetProperty("arguments").Clone();
                Commands.Enqueue(command);
                Arguments.Enqueue((command, arguments));
                object body = Body(command);
                await protocol.WriteAsync(new
                {
                    seq = sequence++,
                    type = "response",
                    request_seq = requestSequence,
                    success = true,
                    command,
                    body,
                }, lifetime.Token);
                if (command == "launch")
                    await EventAsync(protocol, sequence++, "initialized", new { });
                if (command == "configurationDone")
                    await EventAsync(protocol, sequence++, "stopped", new
                    {
                        reason = "breakpoint",
                        threadId = 7,
                        allThreadsStopped = true,
                    });
                if (command == "next")
                    await EventAsync(protocol, sequence++, "stopped", new
                    {
                        reason = "step",
                        threadId = 7,
                        allThreadsStopped = true,
                    });
                if (command == "disconnect")
                {
                    exited = true;
                    adapterOutput.Dispose();
                    return;
                }
            }
        }

        private static (NamedPipeServerStream Writer, NamedPipeClientStream Reader)
            CreatePipePair()
        {
            string name = $"harness-dap-{Guid.NewGuid():N}";
            NamedPipeServerStream writer = new(name, PipeDirection.Out, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            NamedPipeClientStream reader = new(".", name, PipeDirection.In,
                PipeOptions.Asynchronous);
            Task connected = writer.WaitForConnectionAsync();
            reader.Connect();
            connected.GetAwaiter().GetResult();
            return (writer, reader);
        }

        private static object Body(string command) => command switch
        {
            "initialize" => new
            {
                supportsConfigurationDoneRequest = true,
                supportsConditionalBreakpoints = true,
                supportsTerminateRequest = true,
            },
            "setBreakpoints" => new
            {
                breakpoints = new[] { new { id = 4, verified = true, line = 12 } },
            },
            "threads" => new
            {
                threads = new[] { new { id = 7, name = "Main Thread" } },
            },
            "stackTrace" => new
            {
                stackFrames = new[]
                {
                    new
                    {
                        id = 8,
                        name = "Program.Main()",
                        source = new { path = "/workspace/Program.cs" },
                        line = 12,
                        column = 5,
                    },
                },
            },
            "scopes" => new
            {
                scopes = new[]
                {
                    new { name = "Locals", variablesReference = 9, expensive = false },
                },
            },
            "variables" => new
            {
                variables = new[]
                {
                    new
                    {
                        name = "answer",
                        value = "42",
                        type = "int",
                        variablesReference = 0,
                        namedVariables = 0,
                        indexedVariables = 0,
                    },
                },
            },
            _ => new { },
        };

        private static ValueTask EventAsync(
            DapProtocolStream protocol,
            int sequence,
            string name,
            object body) => protocol.WriteAsync(new
            {
                seq = sequence,
                type = "event",
                @event = name,
                body,
            });
    }
}
