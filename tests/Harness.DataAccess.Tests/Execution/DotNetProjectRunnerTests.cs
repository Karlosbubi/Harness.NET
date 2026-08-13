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
