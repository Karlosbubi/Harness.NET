using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.BusinessLogic.Tests.Execution;

public sealed class DeveloperProjectExecutionServiceTests
{
    private const string Source = "class Program { static void Main() { } }\n";

    [Fact]
    public async Task Starts_a_typed_run_and_keeps_raw_output_process_local()
    {
        Runner runner = new();
        Store store = new();
        using DeveloperProjectExecutionService service = CreateService(runner, store);

        DeveloperExecutionStartResult started = await service.StartRunAsync(new(
            new(new("workspace-a"), null), Target()));

        Assert.NotNull(started.Execution);
        Assert.Equal(DeveloperExecutionState.Running, started.Execution.State);
        runner.Complete();
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);
        Assert.Equal(DeveloperExecutionState.Succeeded, completed.State);
        Assert.Equal("synthetic process output", completed.StandardOutput?.Value);
        Assert.True(completed.IsOutputAvailable);
        Assert.DoesNotContain(store.Items.Single().GetType().GetProperties(), property =>
            property.Name is "StandardOutput" or "StandardError");
    }

    [Fact]
    public async Task Rejects_a_stale_source_baseline_before_starting_dotnet()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());
        WorkbenchExecutionTarget stale = Target() with
        {
            SourceBaseline = new(new string('0', 64)),
        };

        DeveloperExecutionStartResult result = await service.StartRunAsync(new(
            new(new("workspace-a"), null), stale));

        Assert.Equal("execution_source_changed", result.ErrorCode);
        Assert.Null(result.Execution);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Cancellation_reaches_the_owned_project_process()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());
        DeveloperExecutionStartResult started = await service.StartRunAsync(new(
            new(new("workspace-a"), null), Target()));

        DeveloperExecutionCancelResult cancelled = await service.CancelAsync(
            started.Execution!.Id);
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);

        Assert.True(cancelled.CancellationRequested);
        Assert.Equal(DeveloperExecutionState.Cancelled, completed.State);
    }

    private static DeveloperProjectExecutionService CreateService(Runner runner, Store store) => new(
        new Context(), new Workspaces(), new DotNet(), new Files(), runner, store,
        new FixedTimeProvider(), NullLogger<DeveloperProjectExecutionService>.Instance);

    private static WorkbenchExecutionTarget Target() => new(
        WorkbenchExecutionTargetKind.ProjectEntryPoint,
        new("App.csproj"), new("net10.0"), new("M:Program.Main"),
        new("Program.cs"), new(Hash(Source)), new(1));

    private static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value)));

    private static async Task<DeveloperExecutionView> WaitForCompletionAsync(
        DeveloperProjectExecutionService service)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            DeveloperExecutionListResult listed = await service.ListAsync(
                new(new("workspace-a"), null));
            DeveloperExecutionView execution = Assert.Single(listed.Executions);
            if (execution.State is not DeveloperExecutionState.Running)
            {
                return execution;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("The synthetic developer run did not complete.");
    }

    private sealed class Context : IWorkbenchWorkspaceContextResolver
    {
        public ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkbenchWorkspaceResolution>(new(
            new(request.WorkspaceId, request.GoalId, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
            "/workspace", null, null));
    }

    private sealed class Workspaces : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(new(
                "workspace-a", "/workspace", "Workspace", "Harness.slnx", true, true,
                "main", false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        public ValueTask<RegisteredWorkspace> SaveAsync(WorkspaceInspection inspection, string entryPoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(string workspaceId, bool trusted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DotNet : IWorkspaceDotNetInspector
    {
        public ValueTask<WorkspaceDotNetInfo> InspectAsync(string workspaceRoot, string entryPoint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceDotNetInfo(
                "Harness.slnx", "slnx", null,
                [new("App.csproj", "Microsoft.NET.Sdk", ["net10.0"], null, null, [])],
                false, null, null));
    }

    private sealed class Files : IWorkspaceFileReader
    {
        public ValueTask<WorkspaceFileRead> ReadAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceFileRead(
                relativePath, Source, Hash(Source), Source.Length, false, null, null));
    }

    private sealed class Runner : IDotNetProjectRunner
    {
        private readonly TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public void Complete() => completion.TrySetResult();

        public async ValueTask<DotNetProjectExecutionResult> RunAsync(
            string sourceRoot,
            DotNetProjectExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            try
            {
                await completion.Task.WaitAsync(cancellationToken);
                return new(request.ProjectPath, request.TargetFramework, 0,
                    new("synthetic process output"), new(string.Empty), false, false,
                    false, 12, null, null);
            }
            catch (OperationCanceledException)
            {
                return new(request.ProjectPath, request.TargetFramework, 137,
                    new(string.Empty), new(string.Empty), false, false,
                    true, 1, "cancelled", "The project run was cancelled.");
            }
        }
    }

    private sealed class Store : IDeveloperDotNetExecutionStore
    {
        private readonly object gate = new();
        public List<StoredDeveloperExecution> Items { get; } = [];

        public ValueTask<StoredDeveloperExecution> StartAsync(StoredDeveloperExecutionStart execution, CancellationToken cancellationToken = default)
        {
            StoredDeveloperExecution stored = new(
                execution.Id, execution.WorkspaceId, execution.GoalId,
                execution.SourceDescription, execution.ProjectPath, execution.TargetFramework,
                execution.DeclarationId, StoredDeveloperExecutionState.Running,
                execution.StartedAt, null, null, 0, null, null);
            lock (gate) Items.Add(stored);
            return ValueTask.FromResult(stored);
        }

        public ValueTask CompleteAsync(StoredDeveloperExecutionCompletion completion, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                int index = Items.FindIndex(item => item.Id == completion.Id);
                Items[index] = Items[index] with
                {
                    State = completion.State,
                    CompletedAt = completion.CompletedAt,
                    ExitCode = completion.ExitCode,
                    DurationMilliseconds = completion.DurationMilliseconds,
                    ErrorCode = completion.ErrorCode,
                    Error = completion.Error,
                };
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<StoredDeveloperExecution>> ListAsync(StoredDeveloperWorkspaceId workspaceId, StoredDeveloperGoalId? goalId, int maximumResults, CancellationToken cancellationToken = default)
        {
            lock (gate) return ValueTask.FromResult<IReadOnlyList<StoredDeveloperExecution>>(
                Items.ToArray());
        }

        public ValueTask<int> InterruptRunningAsync(DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private long ticks;
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-08-13T10:00:00Z").AddTicks(Interlocked.Increment(ref ticks));
    }
}
