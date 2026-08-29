using System.Collections.Immutable;
using System.Text;
using Harness.BusinessLogic.Terminal;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Terminal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.BusinessLogic.Tests.Terminal;

public sealed class DeveloperTerminalServiceTests
{
    [Fact]
    public async Task Trusted_source_context_starts_bounded_transient_terminal_lifecycle()
    {
        FakeConnection connection = new();
        FakeFactory factory = new(connection);
        await using DeveloperTerminalService service = Service(factory);

        DeveloperTerminalStartResult started = await service.StartAsync(new(
            Workspace(),
            new(100, 30)));

        DeveloperTerminalSessionView session = Assert.IsType<DeveloperTerminalSessionView>(
            started.Session);
        Assert.Equal(DeveloperTerminalSessionState.Running, session.State);
        Assert.Equal("bash", session.Shell.Value);
        Assert.Equal(".", session.WorkingDirectory.Value);
        Assert.True(session.IsTrusted);
        Assert.Contains("Transient", session.ContentPolicy.Value, StringComparison.Ordinal);
        Assert.Equal("/workspace", factory.StartRequest!.WorkingDirectory.Value);
        Assert.Equal("xterm-256color", factory.StartRequest.Environment
            .Single(item => item.Name.Value == "TERM").Value.Value);

        byte[] input = Encoding.UTF8.GetBytes("printf hello\n");
        Assert.NotNull((await service.WriteAsync(session.Id, new(input))).Session);
        Assert.Equal(input, connection.Written);

        Assert.NotNull((await service.ResizeAsync(session.Id, new(120, 42))).Session);
        Assert.Equal(new StoredTerminalDimensions(120, 42), connection.Dimensions);

        DeveloperTerminalSessionResult stopped = await service.StopAsync(session.Id);
        Assert.Equal(DeveloperTerminalSessionState.Stopped, stopped.Session!.State);
        Assert.True(connection.StopCalled);
    }

    [Fact]
    public async Task Failed_workspace_resolution_never_resolves_or_starts_a_shell()
    {
        FakeFactory factory = new(new());
        await using DeveloperTerminalService service = new(
            new FakeResolver(new(
                new(new("workspace"), null, null, WorkbenchWorkspaceScope.Unavailable,
                    "Workspace context unavailable"),
                null,
                "workspace_not_trusted",
                "Trust the workspace before inspecting its content.")),
            factory,
            TimeProvider.System,
            NullLogger<DeveloperTerminalService>.Instance);

        DeveloperTerminalStartResult result = await service.StartAsync(new(
            Workspace(),
            new(80, 24)));

        Assert.Null(result.Session);
        Assert.Equal("workspace_not_trusted", result.ErrorCode);
        Assert.Equal(0, factory.ResolveCalls);
    }

    [Fact]
    public async Task Service_caps_live_sessions_and_releases_capacity_after_stop()
    {
        Queue<FakeConnection> connections = new(Enumerable.Range(0, 5)
            .Select(_ => new FakeConnection()));
        QueueFactory factory = new(connections);
        await using DeveloperTerminalService service = Service(factory);
        List<DeveloperTerminalSessionView> started = [];

        for (int index = 0; index < 4; index++)
        {
            started.Add((await service.StartAsync(new(Workspace(), new(80, 24)))).Session!);
        }

        DeveloperTerminalStartResult rejected = await service.StartAsync(new(
            Workspace(),
            new(80, 24)));
        Assert.Equal("terminal_limit_reached", rejected.ErrorCode);

        await service.StopAsync(started[0].Id);
        Assert.NotNull((await service.StartAsync(new(Workspace(), new(80, 24)))).Session);
    }

    private static DeveloperTerminalService Service(IDeveloperTerminalConnectionFactory factory) =>
        new(
            new FakeResolver(new(
                new(new("workspace"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace · user-editable source context"),
                "/workspace",
                null,
                null)),
            factory,
            TimeProvider.System,
            NullLogger<DeveloperTerminalService>.Instance);

    private static WorkbenchWorkspaceRequest Workspace() => new(new("workspace"), null);

    private sealed class FakeResolver(WorkbenchWorkspaceResolution resolution)
        : IWorkbenchWorkspaceContextResolver
    {
        public ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(resolution);
    }

    private class FakeFactory(FakeConnection connection) : IDeveloperTerminalConnectionFactory
    {
        public int ResolveCalls { get; private set; }
        public StoredTerminalStartRequest? StartRequest { get; private set; }

        public ValueTask<StoredTerminalShell> ResolveDefaultShellAsync(
            CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            return ValueTask.FromResult(new StoredTerminalShell(
                new("/bin/bash"), new("bash"), ImmutableArray<StoredTerminalArgument>.Empty));
        }

        public virtual ValueTask<IDeveloperTerminalConnection> StartAsync(
            StoredTerminalStartRequest request,
            CancellationToken cancellationToken = default)
        {
            StartRequest = request;
            return ValueTask.FromResult<IDeveloperTerminalConnection>(connection);
        }
    }

    private sealed class QueueFactory(Queue<FakeConnection> connections)
        : IDeveloperTerminalConnectionFactory
    {
        public ValueTask<StoredTerminalShell> ResolveDefaultShellAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new StoredTerminalShell(new("/bin/bash"), new("bash"), []));

        public ValueTask<IDeveloperTerminalConnection> StartAsync(
            StoredTerminalStartRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDeveloperTerminalConnection>(connections.Dequeue());
    }

    private sealed class FakeConnection : IDeveloperTerminalConnection
    {
        private readonly TaskCompletionSource<StoredTerminalExit> exit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[] Written { get; private set; } = [];
        public StoredTerminalDimensions? Dimensions { get; private set; }
        public bool StopCalled { get; private set; }

        public ValueTask<StoredTerminalReadResult> ReadAsync(
            int maximumBytes,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new StoredTerminalReadResult(new(ReadOnlyMemory<byte>.Empty), true));

        public ValueTask WriteAsync(
            StoredTerminalData data,
            CancellationToken cancellationToken = default)
        {
            Written = data.Value.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(
            StoredTerminalDimensions dimensions,
            CancellationToken cancellationToken = default)
        {
            Dimensions = dimensions;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            exit.TrySetResult(new(129));
            return ValueTask.CompletedTask;
        }

        public async ValueTask<StoredTerminalExit> WaitForExitAsync(
            CancellationToken cancellationToken = default) =>
            await exit.Task.WaitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            exit.TrySetResult(new(129));
            return ValueTask.CompletedTask;
        }
    }
}
