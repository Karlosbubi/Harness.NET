using Harness.DataAccess.Execution;

namespace Harness.DataAccess.Tests.Execution;

public sealed class DotNetProjectRunnerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-project-runner-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Runs_a_confined_project_without_shell_restore_or_launch_profile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), "<Project />");
        string executable = await CreateExecutableAsync("printf '%s\\n' \"$@\"");
        DotNetProjectRunner runner = new(executable);

        DotNetProjectExecutionResult result = await runner.RunAsync(root,
            new(new("App.csproj"), new("net10.0")));

        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("run", result.StandardOutput.Value, StringComparison.Ordinal);
        Assert.Contains("--project", result.StandardOutput.Value, StringComparison.Ordinal);
        Assert.Contains("--no-restore", result.StandardOutput.Value, StringComparison.Ordinal);
        Assert.Contains("--no-launch-profile", result.StandardOutput.Value, StringComparison.Ordinal);
        Assert.Contains("--framework", result.StandardOutput.Value, StringComparison.Ordinal);
        Assert.Contains("net10.0", result.StandardOutput.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Applies_typed_one_run_overrides_without_a_shell()
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), "<Project />");
        string executable = await CreateExecutableAsync(
            "printf 'cwd=%s\\nenv=%s\\n' \"$PWD\" \"$HARNESS_MODE\"; printf '%s\\n' \"$@\"");
        DotNetProjectRunner runner = new(executable);

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("App.csproj"), null,
            RunOverrides: new(
                new("Development"),
                [new("--message"), new("hello world")],
                [new(new("HARNESS_MODE"), new("one-run"))],
                new("src"))));

        Assert.Null(result.Error);
        Assert.Contains($"cwd={Path.Combine(root, "src")}", result.StandardOutput.Value,
            StringComparison.Ordinal);
        Assert.Contains("env=one-run", result.StandardOutput.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-launch-profile", result.StandardOutput.Value,
            StringComparison.Ordinal);
        Assert.Contains("--launch-profile\nDevelopment\n--\n--message\nhello world",
            result.StandardOutput.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_run_overrides_for_non_run_operations()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), "<Project />");
        DotNetProjectRunner runner = new(Path.Combine(root, "missing-dotnet"));

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("App.csproj"), null, DotNetProjectOperation.Build,
            RunOverrides: new(null, [new("argument")], [], null)));

        Assert.Equal("run_overrides_invalid", result.ErrorCode);
        Assert.Null(result.ExitCode);
    }

    [Theory]
    [InlineData("DOTNET_CLI_TELEMETRY_OPTOUT")]
    [InlineData("DOTNET_NOLOGO")]
    public async Task Rejects_environment_overrides_owned_by_the_runner(string name)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), "<Project />");
        DotNetProjectRunner runner = new(Path.Combine(root, "missing-dotnet"));

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("App.csproj"), null,
            RunOverrides: new(null, [], [new(new(name), new("0"))], null)));

        Assert.Equal("run_overrides_invalid", result.ErrorCode);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Starts_hot_reload_as_noninteractive_dotnet_watch_without_browser_launch()
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), "<Project />");
        string executable = await CreateExecutableAsync(
            "printf 'browser=%s\\nrefresh=%s\\nrestart=%s\\n' " +
            "\"$DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER\" " +
            "\"$DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH\" " +
            "\"$DOTNET_WATCH_RESTART_ON_RUDE_EDIT\"; printf '%s\\n' \"$@\"");

        DotNetProjectExecutionResult result = await new DotNetProjectRunner(executable)
            .RunAsync(root, new(
                new("App.csproj"), new("net10.0"), DotNetProjectOperation.HotReload,
                RunOverrides: new(null, [new("app-value")], [], null)));

        Assert.Null(result.Error);
        Assert.Contains("browser=1\nrefresh=1\nrestart=1", result.StandardOutput.Value,
            StringComparison.Ordinal);
        Assert.Contains(
            $"watch\n--non-interactive\n--project\n{Path.Combine(root, "App.csproj")}\n" +
            "run\n--no-restore\n--framework\nnet10.0\n--no-launch-profile\n--\napp-value",
            result.StandardOutput.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_project_outside_the_source_context_before_process_start()
    {
        Directory.CreateDirectory(root);
        DotNetProjectRunner runner = new(Path.Combine(root, "missing-dotnet"));

        DotNetProjectExecutionResult result = await runner.RunAsync(root,
            new(new("../Outside.csproj"), null));

        Assert.Equal("outside_workspace", result.ErrorCode);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Rejects_an_unknown_operation_before_process_start()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), "<Project />");
        DotNetProjectRunner runner = new(Path.Combine(root, "missing-dotnet"));

        DotNetProjectExecutionResult result = await runner.RunAsync(root,
            new(new("App.csproj"), null, (DotNetProjectOperation)999));

        Assert.Equal("operation_invalid", result.ErrorCode);
        Assert.Null(result.ExitCode);
    }

    [Theory]
    [InlineData(DotNetProjectOperation.Build, false)]
    [InlineData(DotNetProjectOperation.Rebuild, true)]
    public async Task Builds_with_a_closed_operation_configuration_and_no_restore(
        DotNetProjectOperation operation,
        bool expectsNoIncremental)
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), "<Project />");
        string executable = await CreateExecutableAsync("printf '%s\\n' \"$@\"");
        DotNetProjectRunner runner = new(executable);

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("App.csproj"),
            new("net10.0"),
            operation,
            new("Any CPU")));

        Assert.Null(result.Error);
        string[] arguments = result.StandardOutput.Value.Split('\n');
        string[] expected = expectsNoIncremental
            ? ["build", Path.Combine(root, "App.csproj"), "--no-restore", "--no-incremental",
                "--framework", "net10.0", "--configuration", "Any CPU"]
            : ["build", Path.Combine(root, "App.csproj"), "--no-restore",
                "--framework", "net10.0", "--configuration", "Any CPU"];
        Assert.Equal(expected, arguments);
    }

    [Fact]
    public async Task Runs_one_exact_test_without_shell_or_restore()
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Tests.csproj"), "<Project />");
        string executable = await CreateExecutableAsync("printf '%s\\n' \"$@\"");
        DotNetProjectRunner runner = new(executable);

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("Tests.csproj"),
            new("net10.0"),
            DotNetProjectOperation.Test,
            new("Release"),
            new("Demo.CalculatorTests.Adds")));

        Assert.Null(result.Error);
        string[] arguments = result.StandardOutput.Value.Split('\n');
        Assert.Equal([
            "test", Path.Combine(root, "Tests.csproj"), "--no-restore", "--filter",
            "FullyQualifiedName=Demo.CalculatorTests.Adds",
        ], arguments[..5]);
        AssertPrivateTrxArguments(arguments, 5);
        Assert.Equal([
            "--framework", "net10.0",
            "--configuration", "Release",
        ], arguments[9..]);
    }

    [Theory]
    [InlineData(DotNetTestScope.Type, "Demo.CalculatorTests", "--filter", "FullyQualifiedName~Demo.CalculatorTests.")]
    [InlineData(DotNetTestScope.Project, "Tests.csproj", null, null)]
    public async Task Runs_a_closed_test_group_in_one_process(
        DotNetTestScope scope,
        string selector,
        string? filterArgument,
        string? filterValue)
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Tests.csproj"), "<Project />");
        string executable = await CreateExecutableAsync("printf '%s\\n' \"$@\"");
        DotNetProjectRunner runner = new(executable);

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("Tests.csproj"),
            null,
            DotNetProjectOperation.Test,
            Test: new(selector),
            TestScope: scope));

        Assert.Null(result.Error);
        List<string> expected = ["test", Path.Combine(root, "Tests.csproj"), "--no-restore"];
        if (filterArgument is not null && filterValue is not null)
        {
            expected.Add(filterArgument);
            expected.Add(filterValue);
        }
        string[] arguments = result.StandardOutput.Value.Split('\n');
        Assert.Equal(expected, arguments[..expected.Count]);
        AssertPrivateTrxArguments(arguments, expected.Count);
    }

    [Fact]
    public async Task Runs_an_exact_test_selection_with_one_internal_filter()
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Tests.csproj"), "<Project />");
        string executable = await CreateExecutableAsync("printf '%s\\n' \"$@\"");
        DotNetProjectRunner runner = new(executable);

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("Tests.csproj"), null, DotNetProjectOperation.Test,
            Test: new("2 selected tests"),
            TestScope: DotNetTestScope.Selection,
            SelectedTests:
            [
                new("Demo.CalculatorTests.Adds"),
                new("Demo.CalculatorTests.Subtracts"),
            ]));

        Assert.Null(result.Error);
        string[] arguments = result.StandardOutput.Value.Split('\n');
        Assert.Equal([
            "test", Path.Combine(root, "Tests.csproj"), "--no-restore", "--filter",
            "FullyQualifiedName=Demo.CalculatorTests.Adds|" +
            "FullyQualifiedName=Demo.CalculatorTests.Subtracts",
        ], arguments[..5]);
        AssertPrivateTrxArguments(arguments, 5);
    }

    [Fact]
    public async Task Collects_adapter_cases_from_a_private_result_directory_then_removes_it()
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Tests.csproj"), "<Project />");
        string executable = await CreateExecutableAsync("""
            results=''
            while [ "$#" -gt 0 ]; do
              if [ "$1" = '--results-directory' ]; then shift; results="$1"; fi
              shift
            done
            mkdir -p "$results"
            printf '%s' '<TestRun><Results><UnitTestResult testId="a" testName="Adds" outcome="Failed" duration="00:00:00.250" /></Results><TestDefinitions><UnitTest id="a"><TestMethod className="Demo.Tests" name="Adds" /></UnitTest></TestDefinitions></TestRun>' > "$results/results.trx"
            """);
        string resultRoot = Path.Combine(root, "private-results");
        DotNetProjectRunner runner = new(executable, resultRoot);

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("Tests.csproj"), null, DotNetProjectOperation.Test,
            Test: new("Demo.Tests.Adds")));

        DotNetTestCaseResult test = Assert.Single(result.TestCases);
        Assert.Equal("Demo.Tests.Adds", test.FullyQualifiedName.Value);
        Assert.Equal(DotNetTestOutcome.Failed, test.Outcome);
        Assert.Equal(250, test.DurationMilliseconds);
        Assert.False(result.AreTestCasesTruncated);
        Assert.Empty(Directory.EnumerateDirectories(resultRoot));
    }

    [Fact]
    public async Task Rejects_an_unbounded_test_filter_before_process_start()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Tests.csproj"), "<Project />");
        DotNetProjectRunner runner = new(Path.Combine(root, "missing-dotnet"));

        DotNetProjectExecutionResult result = await runner.RunAsync(root, new(
            new("Tests.csproj"), null, DotNetProjectOperation.Test,
            Test: new("Demo.Tests.Passes|Other")));

        Assert.Equal("test_name_invalid", result.ErrorCode);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Cancellation_kills_the_project_process_tree()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), "<Project />");
        string executable = await CreateExecutableAsync("sleep 30");
        DotNetProjectRunner runner = new(executable);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));

        DotNetProjectExecutionResult result = await runner.RunAsync(root,
            new(new("App.csproj"), null), cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal("cancelled", result.ErrorCode);
        Assert.NotNull(result.ExitCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<string> CreateExecutableAsync(string command)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The fake dotnet executable is Linux-specific.");
        }
        string path = Path.Combine(root, $"fake-dotnet-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, $"#!/bin/sh\n{command}\n");
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static void AssertPrivateTrxArguments(string[] arguments, int offset)
    {
        Assert.Equal("--logger", arguments[offset]);
        Assert.Equal("trx", arguments[offset + 1]);
        Assert.Equal("--results-directory", arguments[offset + 2]);
        Assert.Contains("harness-test-results", arguments[offset + 3],
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(arguments[offset + 3]));
    }
}
