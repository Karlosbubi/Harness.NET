using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Harness.DataAccess.Terminal;

namespace Harness.DataAccess.Tests.Terminal;

public sealed class PortaDeveloperTerminalConnectionFactoryTests
{
    [Fact]
    [Trait("Category", "Adapter")]
    public async Task Real_linux_pty_round_trips_unicode_resizes_and_stops_the_process_tree()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        PortaDeveloperTerminalConnectionFactory factory = new();
        StoredTerminalShell shell = await factory.ResolveDefaultShellAsync();
        await using IDeveloperTerminalConnection connection = await factory.StartAsync(new(
            new("adapter-test"),
            shell,
            new(Path.GetTempPath()),
            [],
            new(80, 24)));

        await connection.ResizeAsync(new(120, 40));
        await connection.WriteAsync(new(Encoding.UTF8.GetBytes(
            "stty -echo; printf 'REA%s\\n' DY\n")));
        await ReadUntilAsync(connection, "READY", TimeSpan.FromSeconds(10));
        await connection.WriteAsync(new(Encoding.UTF8.GetBytes(
            "sleep 300 & printf 'CHILD:%s\\n' $!; printf 'UNICODE:Grüße-λ\\n'\n")));

        string output = await ReadUntilAsync(connection, "UNICODE:Grüße-λ", TimeSpan.FromSeconds(10));
        int childProcessId = ParseChildProcessId(output);
        Assert.True(ProcessExists(childProcessId));

        await connection.StopAsync();
        await connection.WaitForExitAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await AssertEventuallyAsync(() => !ProcessExists(childProcessId), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Rejects_out_of_bounds_dimensions_before_spawning()
    {
        PortaDeveloperTerminalConnectionFactory factory = new();
        StoredTerminalShell shell = await factory.ResolveDefaultShellAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await factory.StartAsync(new(
                new("invalid-size"),
                shell,
                new(Path.GetTempPath()),
                [],
                new(10, 2))));
    }

    private static async Task<string> ReadUntilAsync(
        IDeveloperTerminalConnection connection,
        string marker,
        TimeSpan timeout)
    {
        using CancellationTokenSource cancellation = new(timeout);
        StringBuilder output = new();
        while (!output.ToString().Contains(marker, StringComparison.Ordinal))
        {
            StoredTerminalReadResult read = await connection.ReadAsync(4_096, cancellation.Token);
            output.Append(Encoding.UTF8.GetString(read.Data.Value.Span));
            if (read.EndOfStream)
            {
                break;
            }
        }

        Assert.Contains(marker, output.ToString(), StringComparison.Ordinal);
        return output.ToString();
    }

    private static int ParseChildProcessId(string output)
    {
        Match match = Regex.Match(output, @"CHILD:(\d+)", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        Assert.True(int.TryParse(match.Groups[1].Value, out int processId));
        return processId;
    }

    private static bool ProcessExists(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task AssertEventuallyAsync(Func<bool> condition, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < timeout)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
    }
}
