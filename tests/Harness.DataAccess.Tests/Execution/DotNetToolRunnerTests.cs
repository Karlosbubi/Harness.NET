using Harness.DataAccess.Execution;

namespace Harness.DataAccess.Tests.Execution;

public sealed class DotNetToolRunnerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-dotnet-tool-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Runs_only_the_typed_operation_without_restore_and_returns_output()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Repository.slnx"), "<Solution />");
        string executable = await CreateExecutableAsync("printf '%s\\n' \"$@\"");
        DotNetToolRunner runner = new(executable);

        DotNetToolResult result = await runner.RunAsync(
            root,
            new("Build", "Repository.slnx"));

        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("build", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("--no-restore", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Repository.slnx", result.StandardOutput, StringComparison.Ordinal);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task Rejects_untyped_operations_before_starting_a_process()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Repository.slnx"), "<Solution />");
        DotNetToolRunner runner = new(Path.Combine(root, "missing-executable"));

        DotNetToolResult result = await runner.RunAsync(
            root,
            new("Restore", "Repository.slnx"));

        Assert.Equal("invalid_operation", result.ErrorCode);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Drains_but_bounds_process_output()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Repository.slnx"), "<Solution />");
        string executable = await CreateExecutableAsync("yes x | head -c 70000");
        DotNetToolRunner runner = new(executable);

        DotNetToolResult result = await runner.RunAsync(
            root,
            new("Test", "Repository.slnx"));

        Assert.Null(result.Error);
        Assert.True(result.IsOutputTruncated);
        Assert.InRange(result.StandardOutput.Length, 60 * 1024, 64 * 1024);
    }

    [Fact]
    public async Task Cancels_the_entire_process_tree_and_returns_evidence()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Repository.slnx"), "<Solution />");
        string executable = await CreateExecutableAsync("sleep 30");
        DotNetToolRunner runner = new(executable);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));

        DotNetToolResult result = await runner.RunAsync(
            root,
            new("Test", "Repository.slnx"),
            cancellation.Token);

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
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
