using Avalonia.Automation;
using Avalonia.Headless;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests.Workbench;

[Collection("Avalonia UI")]
public sealed class DeveloperRunOverrideDialogTests
{
    [Fact]
    public async Task Builds_a_typed_visible_nonpersistent_override_without_exposing_values_in_summary()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperRunOverrideDialog dialog = new("App.csproj");
            dialog.Profile.Text = "Development";
            dialog.WorkingDirectory.Text = "src/App";
            dialog.Arguments.Text = "--message\nhello world";
            dialog.Environment.Text = "HARNESS_MODE=one-run\nTOKEN=private-value";
            dialog.HotReload.IsChecked = true;

            Assert.True(dialog.TryCreate(out var overrides, out string? error), error);
            Assert.Equal("Development", overrides?.LaunchProfile?.Value);
            Assert.Equal(["--message", "hello world"],
                overrides?.Arguments.Select(argument => argument.Value));
            Assert.Equal(["HARNESS_MODE", "TOKEN"],
                overrides?.Environment.Select(variable => variable.Name.Value));
            Assert.Equal("src/App", overrides?.WorkingDirectory?.Value);
            Assert.Contains("arguments: 2", dialog.Summary, StringComparison.Ordinal);
            Assert.Contains("Mode: Hot Reload", dialog.Summary, StringComparison.Ordinal);
            Assert.Contains("HARNESS_MODE, TOKEN", dialog.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("one-run", dialog.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("private-value", dialog.Summary, StringComparison.Ordinal);
            Assert.Equal("One-run launch profile", AutomationProperties.GetName(dialog.Profile));
            Assert.Equal("One-run arguments", AutomationProperties.GetName(dialog.Arguments));
            Assert.Equal("One-run environment", AutomationProperties.GetName(dialog.Environment));
            Assert.Equal("Use Hot Reload for this run",
                AutomationProperties.GetName(dialog.HotReload));
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Rejects_an_environment_line_without_a_name_value_separator()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperRunOverrideDialog dialog = new("App.csproj");
            dialog.Environment.Text = "BROKEN";

            Assert.False(dialog.TryCreate(out _, out string? error));
            Assert.Contains("NAME=value", error, StringComparison.Ordinal);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Debug_purpose_reuses_typed_launch_overrides_without_offering_hot_reload()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperRunOverrideDialog dialog = new(
                "App.csproj", DeveloperRunOverridePurpose.Debug);
            dialog.Arguments.Text = "--inspect";
            dialog.HotReload.IsChecked = true;

            Assert.True(dialog.TryCreate(out var overrides, out string? error), error);
            Assert.Equal("--inspect", Assert.Single(overrides!.Arguments).Value);
            Assert.Contains("Mode: Debug", dialog.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("Hot Reload", dialog.Summary, StringComparison.Ordinal);
            Assert.Null(dialog.HotReload.Parent);
            dialog.Close();
        }, CancellationToken.None);
    }
}
