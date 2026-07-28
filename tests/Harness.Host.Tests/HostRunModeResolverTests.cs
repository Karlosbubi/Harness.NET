namespace Harness.Host.Tests;

public sealed class HostRunModeResolverTests
{
    [Fact]
    public void Uses_interactive_mode_by_default()
    {
        HostRunMode mode = HostRunModeResolver.Resolve(
            [],
            isInputRedirected: false,
            isOutputRedirected: false);

        Assert.Equal(HostRunMode.Interactive, mode);
    }

    [Fact]
    public void Uses_avalonia_by_default_without_attached_streams()
    {
        InteractiveFrontend frontend = HostRunModeResolver.ResolveFrontend(
            [],
            isInputRedirected: true,
            isOutputRedirected: true);

        Assert.Equal(InteractiveFrontend.Avalonia, frontend);
    }

    [Fact]
    public void Selects_terminal_explicitly()
    {
        InteractiveFrontend frontend = HostRunModeResolver.ResolveFrontend(
            ["--ui=terminal"],
            isInputRedirected: false,
            isOutputRedirected: false);

        Assert.Equal(InteractiveFrontend.Terminal, frontend);
    }

    [Fact]
    public void Rejects_terminal_with_redirected_streams() =>
        Assert.Throws<ArgumentException>(() => HostRunModeResolver.ResolveFrontend(
            ["--ui=terminal"],
            isInputRedirected: true,
            isOutputRedirected: false));

    [Fact]
    public void Rejects_ui_with_operational_mode() =>
        Assert.Throws<ArgumentException>(() => HostRunModeResolver.Resolve(
            ["--ui=avalonia", "--no-ui"],
            isInputRedirected: false,
            isOutputRedirected: false));

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
    [InlineData("--ui=avalonia")]
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
