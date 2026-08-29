using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Harness.DataAccess.Debugging;

namespace Harness.DataAccess.Tests.Debugging;

public sealed class DotNetTestDebugSessionFactoryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-test-debug-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Starts_exact_owned_test_and_rechecks_testhost_ancestry_before_attach()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        TestProcess process = new();
        AdapterFactory adapters = new();
        DotNetTestDebugSessionFactory factory = new(
            adapters, new ProgramResolver("/sdk/dotnet"), new ProcessFactory(process));

        await using IDebugAdapterSession session = await factory.StartAsync(root, new(
            new("test_debug_1"),
            new("App.Tests.csproj"),
            new("net10.0"),
            new("Debug"),
            new("Demo.Tests.Exact"),
            JustMyCode: true));

        Assert.Equal(1, process.AncestryChecks);
        Assert.NotNull(adapters.Request);
        Assert.Equal(StoredDebugAdapterStartKind.AttachOwnedProcess, adapters.Request.Kind);
        Assert.Equal(4242, adapters.Request.OwnedProcessId?.Value);
        Assert.Empty(adapters.Request.Arguments);
        Assert.DoesNotContain(process.Arguments, value => value.Contains("--blame",
            StringComparison.Ordinal));
        Assert.Equal(
            ["test", Path.Combine(root, "App.Tests.csproj"), "--no-restore", "--filter",
             "FullyQualifiedName=Demo.Tests.Exact", "--logger", "console;verbosity=minimal",
             "--framework", "net10.0", "--configuration", "Debug"],
            process.Arguments);
    }

    [Fact]
    public async Task Rejects_changed_ancestry_without_attaching_and_cleans_the_owned_process()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.Tests.csproj"), "<Project />");
        TestProcess process = new() { IsDescendant = false };
        AdapterFactory adapters = new();
        DotNetTestDebugSessionFactory factory = new(
            adapters, new ProgramResolver("/sdk/dotnet"), new ProcessFactory(process));

        await Assert.ThrowsAsync<DebugAdapterRequestException>(async () =>
            await factory.StartAsync(root, new(
                new("test_debug_2"), new("App.Tests.csproj"), null, null,
                new("Demo.Tests.Exact"), true)));

        Assert.Null(adapters.Request);
        Assert.True(process.Disposed);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    [Trait("Tier", "Live")]
    public async Task Pinned_netcoredbg_attaches_only_to_owned_waiting_testhost()
    {
        string? adapterPath = Environment.GetEnvironmentVariable("HARNESS_NETCOREDBG_LIVE_PATH");
        string? repository = Environment.GetEnvironmentVariable("HARNESS_REPOSITORY_ROOT");
        if (adapterPath is null || repository is null || !OperatingSystem.IsLinux()) return;
        NetCoreDbgAdapterSessionFactory adapterFactory = new(
            new ExecutableResolver(adapterPath));
        DotNetTestDebugSessionFactory factory = new(adapterFactory);
        await using IDebugAdapterSession session = await factory.StartAsync(repository, new(
            new("live_test_debug"),
            new("tests/Harness.DataAccess.Tests/Harness.DataAccess.Tests.csproj"),
            new("net10.0"),
            null,
            new("Harness.DataAccess.Tests.Debugging.DapProtocolStreamTests.Writes_one_exact_utf8_frame"),
            true));
        IReadOnlyList<StoredDebugBreakpoint> breakpoints = await session.SetBreakpointsAsync(
            new("tests/Harness.DataAccess.Tests/Debugging/DapProtocolStreamTests.cs"),
            [new(new("tests/Harness.DataAccess.Tests/Debugging/DapProtocolStreamTests.cs"),
                new(32))]);
        await session.CompleteConfigurationAsync();
        StoredDebugEvent stopped = await EventAsync(session, StoredDebugEventKind.Stopped);
        await session.ContinueAsync(stopped.ThreadId!);
        StoredDebugEvent terminated = await EventAsync(session, StoredDebugEventKind.Terminated);

        Assert.Equal(StoredDebugStopReason.Breakpoint, stopped.StopReason);
        Assert.Single(breakpoints);
        Assert.Equal(StoredDebugEventKind.Terminated, terminated.Kind);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static async Task<StoredDebugEvent> EventAsync(
        IDebugAdapterSession session,
        StoredDebugEventKind kind)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(90));
        await foreach (StoredDebugEvent value in session.ReadEventsAsync(timeout.Token))
            if (value.Kind == kind) return value;
        throw new InvalidOperationException($"No {kind} event was received.");
    }

    private sealed class ProgramResolver(string path) : IDotNetDebugProgramResolver
    {
        public string Resolve() => path;
    }

    private sealed class ProcessFactory(TestProcess process) : IOwnedDotNetTestProcessFactory
    {
        public IOwnedDotNetTestProcess Start(
            string dotNetExecutable,
            string workingDirectory,
            IReadOnlyList<string> arguments)
        {
            Assert.Equal("/sdk/dotnet", dotNetExecutable);
            process.Arguments = arguments;
            return process;
        }
    }

    private sealed class TestProcess : IOwnedDotNetTestProcess
    {
        public StoredDebugProcessId RootProcessId { get; } = new(3131);
        internal IReadOnlyList<string> Arguments { get; set; } = [];
        internal bool IsDescendant { get; init; } = true;
        internal int AncestryChecks { get; private set; }
        internal bool Disposed { get; private set; }

        public ValueTask<StoredDebugProcessId> WaitForTestHostAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(new StoredDebugProcessId(4242));

        public bool IsLiveDescendant(StoredDebugProcessId processId)
        {
            AncestryChecks++;
            return IsDescendant;
        }

        public ValueTask<OwnedDotNetTestResult> WaitForExitAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(new OwnedDotNetTestResult(0, "passed"));

        public void Kill() { }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AdapterFactory : IDebugAdapterSessionFactory
    {
        internal StoredDebugAdapterStartRequest? Request { get; private set; }

        public ValueTask<IDebugAdapterSession> StartAsync(
            StoredDebugAdapterStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult<IDebugAdapterSession>(new AdapterSession());
        }
    }

    private sealed class AdapterSession : IDebugAdapterSession
    {
        public StoredDebugSessionId Id { get; } = new("adapter");
        public StoredDebugAdapterCapabilities Capabilities { get; } = new(true, true, true, true);

        public async IAsyncEnumerable<StoredDebugEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<IReadOnlyList<StoredDebugBreakpoint>> SetBreakpointsAsync(
            StoredDebugSourcePath source, IReadOnlyList<StoredDebugBreakpointRequest> breakpoints,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<
                IReadOnlyList<StoredDebugBreakpoint>>([]);
        public ValueTask CompleteConfigurationAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<StoredDebugThread>> GetThreadsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<StoredDebugThread>>([]);
        public ValueTask<IReadOnlyList<StoredDebugStackFrame>> GetStackTraceAsync(StoredDebugThreadId threadId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<StoredDebugStackFrame>>([]);
        public ValueTask<IReadOnlyList<StoredDebugScope>> GetScopesAsync(StoredDebugStackFrameId frameId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<StoredDebugScope>>([]);
        public ValueTask<IReadOnlyList<StoredDebugVariable>> GetVariablesAsync(StoredDebugVariablesReference variablesReference, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<StoredDebugVariable>>([]);
        public ValueTask ContinueAsync(StoredDebugThreadId threadId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask PauseAsync(StoredDebugThreadId threadId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask StepOverAsync(StoredDebugThreadId threadId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask StepInAsync(StoredDebugThreadId threadId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask StepOutAsync(StoredDebugThreadId threadId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisconnectAsync(bool terminateDebuggee, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ExecutableResolver(string path) : IDebugAdapterExecutableResolver
    {
        public ValueTask<string?> ResolveVerifiedExecutableAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(path);
    }
}
