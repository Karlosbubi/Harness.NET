using Harness.Presentation.Terminal;

namespace Harness.Presentation.Terminal.Tests;

public sealed class ShellLayoutPolicyTests
{
    [Theory]
    [InlineData(140, "Wide", true, true)]
    [InlineData(100, "Compact", true, false)]
    [InlineData(70, "Narrow", false, false)]
    public void Collapses_regions_as_terminal_width_decreases(
        int width,
        string expectedMode,
        bool expectedWorkspace,
        bool expectedDetails)
    {
        ShellLayout layout = ShellLayoutPolicy.ForWidth(width);

        Assert.Equal(expectedMode, layout.Mode.ToString());
        Assert.Equal(expectedWorkspace, layout.ShowWorkspace);
        Assert.Equal(expectedDetails, layout.ShowDetails);
    }
}
