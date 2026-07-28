namespace Harness.Host.Tests;

public sealed class HostRunModeResolverTests
{
    [Fact]
    public void Uses_interactive_mode_for_an_attached_terminal()
    {
        HostRunMode mode = HostRunModeResolver.Resolve(
            [],
            isInputRedirected: false,
            isOutputRedirected: false);

        Assert.Equal(HostRunMode.Interactive, mode);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Uses_initialize_mode_for_redirected_streams(
        bool inputRedirected,
        bool outputRedirected)
    {
        HostRunMode mode = HostRunModeResolver.Resolve(
            [],
            inputRedirected,
            outputRedirected);

        Assert.Equal(HostRunMode.Initialize, mode);
    }

    [Fact]
    public void Explicit_wait_mode_takes_precedence_over_redirection()
    {
        HostRunMode mode = HostRunModeResolver.Resolve(
            [HostRunModeResolver.WaitForShutdownArgument],
            isInputRedirected: true,
            isOutputRedirected: true);

        Assert.Equal(HostRunMode.WaitForShutdown, mode);
    }

    [Theory]
    [InlineData("--no-ui")]
    [InlineData("--wait-for-shutdown")]
    [InlineData("--backup-path=/tmp/harness.zip")]
    public void Recognizes_operational_arguments(string argument)
    {
        Assert.True(HostRunModeResolver.IsOperationalArgument(argument));
    }

    [Fact]
    public void Extracts_noninteractive_backup_destination()
    {
        string? path = HostRunModeResolver.BackupPath(
            ["--backup-path=/tmp/harness.zip"]);

        Assert.Equal("/tmp/harness.zip", path);
    }
}
