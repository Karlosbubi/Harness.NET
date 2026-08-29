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
    public async Task Debug_target_reuses_the_exact_Roslyn_entry_point_revalidation_lifecycle()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());
        IDeveloperExecutionTargetResolver resolver = service;

        DeveloperExecutionTargetResolution resolved = await resolver.ResolveDebugTargetAsync(
            new(new("workspace-a"), null), Target(), DeveloperRunOverrides.None);
        DeveloperExecutionTargetResolution stale = await resolver.ResolveDebugTargetAsync(
            new(new("workspace-a"), null), Target() with
            {
                SourceBaseline = new(new string('0', 64)),
            }, DeveloperRunOverrides.None);

        Assert.Equal("/workspace", resolved.RootPath);
        Assert.Equal("App.csproj", resolved.Project?.Path);
        Assert.Null(resolved.Error);
        Assert.Equal("execution_source_changed", stale.ErrorCode);
        Assert.Null(stale.RootPath);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Test_debug_target_requires_exact_Roslyn_identity_and_source_location()
    {
        using DeveloperProjectExecutionService service = CreateService(new Runner(), new Store());
        IDeveloperExecutionTargetResolver resolver = service;
        DeveloperTestTarget test = new(
            new(new string('a', 64)), new("Demo.Tests.Exact"));

        DeveloperTestDebugTargetResolution result = await resolver.ResolveTestDebugTargetAsync(
            new(new("workspace-a"), null),
            new(new("App.csproj"), new("net10.0"), null),
            test);

        Assert.Equal("/workspace", result.RootPath);
        Assert.Equal("Tests/Exact.cs", result.Source?.Value);
        Assert.Equal(21, result.Line?.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Validates_and_forwards_typed_nonpersistent_one_run_overrides()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());
        DeveloperRunOverrides overrides = new(
            new("Development"),
            [new("--message"), new("hello")],
            [new(new("HARNESS_MODE"), new("one-run"))],
            new("src"));

        DeveloperExecutionStartResult started = await service.StartRunAsync(new(
            new(new("workspace-a"), null), Target(), overrides));

        Assert.NotNull(started.Execution);
        Assert.Same(overrides, started.Execution.RunOverrides);
        Assert.Equal("Development", runner.LastRequest?.RunOverrides?.LaunchProfile?.Value);
        Assert.Equal(["--message", "hello"],
            runner.LastRequest?.RunOverrides?.Arguments.Select(item => item.Value));
        Assert.Equal("HARNESS_MODE",
            Assert.Single(runner.LastRequest!.RunOverrides!.Environment).Name.Value);
        runner.Complete();
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);
        Assert.Null(completed.RunOverrides);
    }

    [Fact]
    public async Task Rejects_a_launch_profile_not_in_the_exact_inspected_project()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());

        DeveloperExecutionStartResult result = await service.StartRunAsync(new(
            new(new("workspace-a"), null), Target(),
            new(new("Uninspected"), [], [], null)));

        Assert.Equal("run_overrides_invalid", result.ErrorCode);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Starts_hot_reload_as_a_distinct_durable_cancellable_lifecycle()
    {
        Runner runner = new();
        Store store = new();
        using DeveloperProjectExecutionService service = CreateService(runner, store);

        DeveloperExecutionStartResult started = await service.StartRunAsync(new(
            new(new("workspace-a"), null), Target(), DeveloperRunOverrides.None,
            DeveloperRunMode.HotReload));

        Assert.Equal(DeveloperExecutionOperation.HotReload, started.Execution?.Operation);
        Assert.Equal(DotNetProjectOperation.HotReload, runner.LastRequest?.Operation);
        Assert.Equal(StoredDeveloperExecutionOperation.HotReload,
            Assert.Single(store.Items).Operation);
        DeveloperExecutionCancelResult cancellation = await service.CancelAsync(
            started.Execution!.Id);
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);
        Assert.True(cancellation.CancellationRequested);
        Assert.Equal(DeveloperExecutionState.Cancelled, completed.State);
        Assert.Equal(DeveloperExecutionOperation.HotReload, completed.Operation);
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

    [Theory]
    [InlineData(DeveloperExecutionOperation.Build)]
    [InlineData(DeveloperExecutionOperation.Rebuild)]
    public async Task Starts_a_typed_project_build_with_the_inspected_configuration(
        DeveloperExecutionOperation operation)
    {
        Runner runner = new();
        Store store = new();
        using DeveloperProjectExecutionService service = CreateService(runner, store);

        DeveloperExecutionStartResult started = await service.StartBuildAsync(new(
            new(new("workspace-a"), null),
            operation,
            new(new("App.csproj"), null, new("Release"))));

        Assert.NotNull(started.Execution);
        Assert.Equal(operation, started.Execution.Operation);
        Assert.Null(started.Execution.EntryPoint);
        Assert.Equal("Release", started.Execution.Project.Configuration?.Value);
        Assert.Equal(
            operation is DeveloperExecutionOperation.Build
                ? DotNetProjectOperation.Build
                : DotNetProjectOperation.Rebuild,
            runner.LastRequest?.Operation);
        Assert.Equal("Release", runner.LastRequest?.Configuration?.Value);
        runner.Complete();
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);
        Assert.Equal(DeveloperExecutionState.Succeeded, completed.State);
        Assert.Equal(operation, store.Items.Single().Operation switch
        {
            StoredDeveloperExecutionOperation.Build => DeveloperExecutionOperation.Build,
            StoredDeveloperExecutionOperation.Rebuild => DeveloperExecutionOperation.Rebuild,
            _ => DeveloperExecutionOperation.Run,
        });
    }

    [Fact]
    public async Task Rejects_an_uninspected_build_configuration_before_starting_dotnet()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());

        DeveloperExecutionStartResult result = await service.StartBuildAsync(new(
            new(new("workspace-a"), null),
            DeveloperExecutionOperation.Build,
            new(new("App.csproj"), null, new("Production"))));

        Assert.Equal("execution_configuration_unavailable", result.ErrorCode);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Starts_one_compiler_discovered_test_with_durable_identity()
    {
        Runner runner = new();
        Store store = new();
        using DeveloperProjectExecutionService service = CreateService(runner, store);
        DeveloperTestTarget test = new(
            new(new string('a', 64)),
            new("Demo.CalculatorTests.Adds"));

        DeveloperExecutionStartResult started = await service.StartTestAsync(new(
            new(new("workspace-a"), null),
            new(new("App.csproj"), new("net10.0"), new("Release")),
            test));

        Assert.Equal(DeveloperExecutionOperation.Test, started.Execution?.Operation);
        Assert.Equal(test, started.Execution?.Test);
        Assert.Null(started.Execution?.EntryPoint);
        Assert.Equal(DotNetProjectOperation.Test, runner.LastRequest?.Operation);
        Assert.Equal(test.FullyQualifiedName.Value, runner.LastRequest?.Test?.Value);
        runner.Complete();
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);
        Assert.Equal(test, completed.Test);
        Assert.Equal(StoredDeveloperExecutionOperation.Test, Assert.Single(store.Items).Operation);
        Assert.Equal(test.Id.Value, Assert.Single(store.Items).TestId?.Value);
    }

    [Theory]
    [InlineData(DeveloperTestScope.Type)]
    [InlineData(DeveloperTestScope.Project)]
    public async Task Starts_one_typed_group_process_with_durable_scope(
        DeveloperTestScope scope)
    {
        Runner runner = new();
        Store store = new();
        using DeveloperProjectExecutionService service = CreateService(runner, store);
        DeveloperProjectPath projectPath = new("App.csproj");
        DeveloperTestTarget test = scope is DeveloperTestScope.Type
            ? DeveloperTestTarget.ForType(projectPath, new("Demo.CalculatorTests"))
            : DeveloperTestTarget.ForProject(projectPath);

        DeveloperExecutionStartResult started = await service.StartTestAsync(new(
            new(new("workspace-a"), null),
            new(projectPath, new("net10.0"), new("Release")),
            test));

        Assert.Equal(test, started.Execution?.Test);
        Assert.Equal(scope is DeveloperTestScope.Type
            ? DotNetTestScope.Type
            : DotNetTestScope.Project, runner.LastRequest?.TestScope);
        runner.Complete();
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);
        Assert.Equal(test, completed.Test);
        Assert.Equal(scope is DeveloperTestScope.Type
            ? StoredDeveloperTestScope.Type
            : StoredDeveloperTestScope.Project, Assert.Single(store.Items).TestScope);
    }

    [Fact]
    public async Task Rejects_a_forged_group_identity_before_starting_dotnet()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());

        DeveloperExecutionStartResult result = await service.StartTestAsync(new(
            new(new("workspace-a"), null),
            new(new("App.csproj"), null, null),
            new(new(new string('a', 64)), new("Demo.CalculatorTests"),
                DeveloperTestScope.Type)));

        Assert.Equal("invalid_test_target", result.ErrorCode);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Starts_one_process_for_a_deterministic_exact_test_selection()
    {
        Runner runner = new();
        Store store = new();
        using DeveloperProjectExecutionService service = CreateService(runner, store);
        DeveloperProjectPath projectPath = new("App.csproj");
        DeveloperTestTarget selection = DeveloperTestTarget.ForSelection(projectPath,
        [
            new("Demo.CalculatorTests.Subtracts"),
            new("Demo.CalculatorTests.Adds"),
        ]);

        DeveloperExecutionStartResult started = await service.StartTestAsync(new(
            new(new("workspace-a"), null),
            new(projectPath, null, null),
            selection));

        Assert.NotNull(started.Execution);
        Assert.Equal(1, runner.Calls);
        Assert.Equal(DotNetTestScope.Selection, runner.LastRequest?.TestScope);
        Assert.Equal([
            "Demo.CalculatorTests.Adds", "Demo.CalculatorTests.Subtracts",
        ], runner.LastRequest?.SelectedTests.Select(item => item.Value));
        runner.Complete();
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);
        Assert.Equal(selection.Id, completed.Test?.Id);
        Assert.Equal(selection.SelectedTests, completed.Test?.SelectedTests);
        DeveloperTestCaseResult testCase = Assert.Single(completed.TestCases);
        Assert.Equal(DeveloperTestOutcome.Passed, testCase.Outcome);
        Assert.Equal("Demo.CalculatorTests.Adds", testCase.FullyQualifiedName.Value);
    }

    [Fact]
    public async Task Rejects_a_non_discovery_test_identity_before_starting_dotnet()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());

        DeveloperExecutionStartResult result = await service.StartTestAsync(new(
            new(new("workspace-a"), null),
            new(new("App.csproj"), null, null),
            new(new("not-a-discovery-hash"), new("Demo.Tests.Passes|Other"))));

        Assert.Equal("invalid_test_target", result.ErrorCode);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Selected_test_cancellation_reaches_the_owned_process_tree()
    {
        Runner runner = new();
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());
        DeveloperExecutionStartResult started = await service.StartTestAsync(TestRequest());

        DeveloperExecutionCancelResult cancellation = await service.CancelAsync(
            started.Execution!.Id);
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);

        Assert.True(cancellation.CancellationRequested);
        Assert.Equal(DotNetProjectOperation.Test, runner.LastRequest?.Operation);
        Assert.Equal(DeveloperExecutionState.Cancelled, completed.State);
    }

    [Fact]
    public async Task Selected_test_failure_keeps_duration_and_exit_history()
    {
        Runner runner = new() { Fail = true };
        using DeveloperProjectExecutionService service = CreateService(runner, new Store());
        await service.StartTestAsync(TestRequest());

        runner.Complete();
        DeveloperExecutionView completed = await WaitForCompletionAsync(service);

        Assert.Equal(DeveloperExecutionState.Failed, completed.State);
        Assert.Equal(1, completed.ExitCode);
        Assert.Equal(12, completed.DurationMilliseconds);
        Assert.Equal("test failure", completed.StandardError?.Value);
    }

    [Fact]
    public async Task Reconstructs_safe_adapter_case_history_without_raw_display_content()
    {
        Store store = new();
        store.Items.Add(new(
            new("test-history"), new("workspace-a"), null, new("Original workspace"),
            StoredDeveloperExecutionOperation.Test, new("App.csproj"), null, null, null,
            StoredDeveloperExecutionState.Succeeded,
            DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-29T12:00:01Z"), 0, 1_000, null, null,
            new(new string('d', 64)), new("Demo.Tests.Passes"),
            StoredDeveloperTestScope.Exact, [],
            [new(new("Demo.Tests.Passes"), StoredDeveloperTestOutcome.Passed, 125)]));
        using DeveloperProjectExecutionService service = CreateService(new Runner(), store);

        DeveloperExecutionView execution = Assert.Single((await service.ListAsync(
            new(new("workspace-a"), null))).Executions);

        DeveloperTestCaseResult testCase = Assert.Single(execution.TestCases);
        Assert.Equal(DeveloperTestOutcome.Passed, testCase.Outcome);
        Assert.Equal(testCase.FullyQualifiedName.Value, testCase.DisplayName.Value);
        Assert.False(execution.IsOutputAvailable);
    }

    [Fact]
    public async Task Reconstructs_durable_test_debug_identity_without_transient_debug_data()
    {
        Store store = new();
        store.Items.Add(new(
            new("debug-history"), new("workspace-a"), null, new("Original workspace"),
            StoredDeveloperExecutionOperation.Debug, new("App.Tests.csproj"),
            new("net10.0"), null, null, StoredDeveloperExecutionState.Interrupted,
            DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-29T12:00:01Z"), null, 1_000,
            "application_restarted", "Harness.NET restarted before this operation completed.",
            new(new string('d', 64)), new("Demo.Tests.Exact"),
            StoredDeveloperTestScope.Exact));
        using DeveloperProjectExecutionService service = CreateService(new Runner(), store);

        DeveloperExecutionView execution = Assert.Single((await service.ListAsync(
            new(new("workspace-a"), null))).Executions);

        Assert.Equal(DeveloperExecutionOperation.Debug, execution.Operation);
        Assert.Equal("Demo.Tests.Exact", execution.Test?.FullyQualifiedName.Value);
        Assert.False(execution.IsOutputAvailable);
        Assert.Empty(execution.TestCases);
    }

    private static DeveloperProjectExecutionService CreateService(Runner runner, Store store) => new(
        new Context(), new Workspaces(), new DotNet(), new Files(), runner, store,
        new FixedTimeProvider(), NullLogger<DeveloperProjectExecutionService>.Instance,
        testIdentityVerifier: new TestIdentityVerifier());

    private static WorkbenchExecutionTarget Target() => new(
        WorkbenchExecutionTargetKind.ProjectEntryPoint,
        new("App.csproj"), new("net10.0"), new("M:Program.Main"),
        new("Program.cs"), new(Hash(Source)), new(1));

    private static DeveloperTestStartRequest TestRequest() => new(
        new(new("workspace-a"), null),
        new(new("App.csproj"), new("net10.0"), new("Release")),
        new(new(new string('a', 64)), new("Demo.CalculatorTests.Adds")));

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
                [new(
                    "App.csproj", "Microsoft.NET.Sdk", ["net10.0"], null, null, [],
                    new(
                        DotNetProjectKind.Executable,
                        [
                            new(new("Debug"), DotNetConfigurationSource.Convention),
                            new(new("Release"), DotNetConfigurationSource.Convention),
                        ],
                        IsStartupCandidate: true,
                        LaunchProfiles: new(
                            [new(new("Development"), DotNetLaunchProfileKind.Project,
                                false, false, [])], null, null)))],
                false, null, null));
    }

    private sealed class Files : IWorkspaceFileReader
    {
        public ValueTask<WorkspaceFileRead> ReadAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceFileRead(
                relativePath, Source, Hash(Source), Source.Length, false, null, null));
    }

    private sealed class TestIdentityVerifier : IDeveloperTestIdentityVerifier
    {
        public ValueTask<DeveloperTestIdentityVerification> VerifyExactAsync(
            WorkbenchWorkspaceRequest workspace,
            DeveloperProjectTarget project,
            DeveloperTestTarget test,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DeveloperTestIdentityVerification>(new(
                true, new DeveloperTestSourcePath("Tests/Exact.cs"),
                new DeveloperTestSourceLine(21), null, null));
    }

    private sealed class Runner : IDotNetProjectRunner
    {
        private readonly TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public DotNetProjectExecutionRequest? LastRequest { get; private set; }
        public bool Fail { get; init; }
        public void Complete() => completion.TrySetResult();

        public async ValueTask<DotNetProjectExecutionResult> RunAsync(
            string sourceRoot,
            DotNetProjectExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            try
            {
                await completion.Task.WaitAsync(cancellationToken);
                return new(request.ProjectPath, request.TargetFramework, Fail ? 1 : 0,
                    new("synthetic process output"), new(Fail ? "test failure" : string.Empty), false, false,
                    false, 12, null, null,
                    request.Operation is DotNetProjectOperation.Test
                        ? [new(new("Demo.CalculatorTests.Adds"), new("Adds"),
                            DotNetTestOutcome.Passed, 5)]
                        : []);
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
                execution.SourceDescription, execution.Operation, execution.ProjectPath,
                execution.TargetFramework, execution.Configuration, execution.DeclarationId,
                StoredDeveloperExecutionState.Running,
                execution.StartedAt, null, null, 0, null, null,
                execution.TestId, execution.TestName, execution.TestScope,
                execution.SelectedTests);
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
                    TestCases = completion.TestCases,
                    AreTestCasesTruncated = completion.AreTestCasesTruncated,
                };
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<StoredDeveloperExecution>> ListAsync(StoredDeveloperWorkspaceId workspaceId, StoredDeveloperGoalId? goalId, int maximumResults, CancellationToken cancellationToken = default)
        {
            lock (gate) return ValueTask.FromResult<IReadOnlyList<StoredDeveloperExecution>>(
                Items.ToArray());
        }

        public ValueTask<int> InterruptRunningAsync(
            DateTimeOffset completedAt,
            DateTimeOffset startedBefore,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private long ticks;
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-08-13T10:00:00Z").AddTicks(Interlocked.Increment(ref ticks));
    }
}
