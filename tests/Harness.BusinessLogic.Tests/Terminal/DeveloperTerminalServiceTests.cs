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
        FakeSessionStore store = new();
        await using DeveloperTerminalService service = Service(factory, store);

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
        Assert.Equal(StoredTerminalSessionState.Stopped,
            store.Sessions.Single().State);
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
            new FakeSessionStore(),
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

    [Fact]
    public async Task Persistence_failure_prevents_an_untracked_shell_from_starting()
    {
        FakeFactory factory = new(new());
        FakeSessionStore store = new() { FailStart = true };
        await using DeveloperTerminalService service = Service(factory, store);

        DeveloperTerminalStartResult result = await service.StartAsync(new(
            Workspace(), new(80, 24)));

        Assert.Null(result.Session);
        Assert.Equal("terminal_persistence_failed", result.ErrorCode);
        Assert.Null(factory.StartRequest);
    }

    [Fact]
    public async Task Restart_reconciles_saved_running_metadata_without_starting_a_shell()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow.AddMinutes(-5);
        FakeSessionStore store = new();
        store.Seed(new(
            new("saved-terminal"), new("workspace"), null,
            StoredTerminalSourceScope.OriginalWorkspace, new("main"),
            new("Original workspace · user-editable source context"), new("."), new("bash"),
            StoredTerminalEnvironmentProfile.InheritedLocked,
            StoredTerminalContentPolicy.Transient, new(100, 30),
            StoredTerminalSessionState.Running, started, null, null, null, null));
        FakeFactory factory = new(new());
        await using DeveloperTerminalService service = Service(factory, store);

        DeveloperTerminalSessionView restored = Assert.Single(
            (await service.ListAsync(Workspace())).Sessions);

        Assert.Equal(DeveloperTerminalSessionState.Interrupted, restored.State);
        Assert.Contains("expired", restored.ContentPolicy.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("application_restarted", restored.ErrorCode);
        Assert.Equal(0, factory.ResolveCalls);
        Assert.Equal(1, store.InterruptCalls);
    }

    private static DeveloperTerminalService Service(
        IDeveloperTerminalConnectionFactory factory,
        FakeSessionStore? store = null) =>
        new(
            new FakeResolver(new(
                new(new("workspace"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace · user-editable source context"),
                "/workspace",
                null,
                null)),
            factory,
            store ?? new FakeSessionStore(),
            TimeProvider.System,
            NullLogger<DeveloperTerminalService>.Instance);

    private static WorkbenchWorkspaceRequest Workspace() => new(new("workspace"), null);

    private sealed class FakeSessionStore : IDeveloperTerminalSessionStore
    {
        private readonly Dictionary<string, StoredTerminalSession> sessions =
            new(StringComparer.Ordinal);

        public bool FailStart { get; init; }
        public int InterruptCalls { get; private set; }
        public IReadOnlyCollection<StoredTerminalSession> Sessions => sessions.Values;

        public void Seed(StoredTerminalSession session) => sessions.Add(session.Id.Value, session);

        public ValueTask<StoredTerminalSession> StartAsync(
            StoredTerminalSessionStart session,
            CancellationToken cancellationToken = default)
        {
            if (FailStart) throw new InvalidOperationException("simulated persistence failure");
            StoredTerminalSession stored = new(
                session.Id, session.WorkspaceId, session.GoalId, session.SourceScope,
                session.SourceBranch, session.SourceDescription, session.WorkingDirectory,
                session.Shell, session.EnvironmentProfile, session.ContentPolicy,
                session.Dimensions, StoredTerminalSessionState.Running, session.StartedAt,
                null, null, null, null);
            sessions.Add(session.Id.Value, stored);
            return ValueTask.FromResult(stored);
        }

        public ValueTask CompleteAsync(
            StoredTerminalSessionCompletion completion,
            CancellationToken cancellationToken = default)
        {
            StoredTerminalSession current = sessions[completion.Id.Value];
            sessions[completion.Id.Value] = current with
            {
                State = completion.State,
                CompletedAt = completion.CompletedAt,
                ExitCode = completion.ExitCode,
                ErrorCode = completion.ErrorCode,
                Error = completion.Error,
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateDimensionsAsync(
            StoredTerminalSessionId sessionId,
            StoredTerminalDimensions dimensions,
            CancellationToken cancellationToken = default)
        {
            sessions[sessionId.Value] = sessions[sessionId.Value] with
            {
                Dimensions = dimensions,
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask<StoredTerminalSession?> GetAsync(
            StoredTerminalSessionId sessionId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                sessions.GetValueOrDefault(sessionId.Value));

        public ValueTask<IReadOnlyList<StoredTerminalSession>> ListAsync(
            StoredTerminalWorkspaceId workspaceId,
            StoredTerminalGoalId? goalId,
            int maximumResults,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<
                IReadOnlyList<StoredTerminalSession>>(sessions.Values
                    .Where(item => item.WorkspaceId == workspaceId && item.GoalId == goalId)
                    .Take(maximumResults)
                    .ToArray());

        public ValueTask<int> InterruptRunningAsync(
            DateTimeOffset completedAt,
            DateTimeOffset startedBefore,
            CancellationToken cancellationToken = default)
        {
            InterruptCalls++;
            int changed = 0;
            foreach ((string id, StoredTerminalSession session) in sessions.ToArray())
            {
                if (session.State != StoredTerminalSessionState.Running ||
                    session.StartedAt >= startedBefore)
                {
                    continue;
                }

                sessions[id] = session with
                {
                    State = StoredTerminalSessionState.Interrupted,
                    CompletedAt = completedAt,
                    ErrorCode = "application_restarted",
                    Error = "Harness.NET restarted before this terminal session completed.",
                };
                changed++;
            }

            return ValueTask.FromResult(changed);
        }
    }

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
