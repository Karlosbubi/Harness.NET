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
        Assert.Equal([
            "test", Path.Combine(root, "Tests.csproj"), "--no-restore", "--filter",
            "FullyQualifiedName=Demo.CalculatorTests.Adds", "--framework", "net10.0",
            "--configuration", "Release",
        ], result.StandardOutput.Value.Split('\n'));
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
        Assert.Equal(expected, result.StandardOutput.Value.Split('\n'));
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
        Assert.Equal([
            "test", Path.Combine(root, "Tests.csproj"), "--no-restore", "--filter",
            "FullyQualifiedName=Demo.CalculatorTests.Adds|" +
            "FullyQualifiedName=Demo.CalculatorTests.Subtracts",
        ], result.StandardOutput.Value.Split('\n'));
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
}
