using Avalonia.Controls;
using Avalonia.Headless;
using Harness.BusinessLogic.Mcp;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AvaloniaInboundMcpUiBridgeTests
{
    [Fact]
    public async Task Unattached_bridge_reports_closed_actions_without_a_frame()
    {
        AvaloniaInboundMcpUiBridge bridge = new(TimeProvider.System);

        InboundUiSnapshot snapshot = await bridge.InspectAsync(false);

        Assert.NotNull(snapshot.Error);
        Assert.Null(snapshot.RenderedFrame);
        Assert.All(snapshot.Actions, action => Assert.False(action.IsAvailable));
    }

    [Fact]
    public async Task Isolated_owned_frame_is_a_bounded_png_of_the_harness_window()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Window window = new()
            {
                Width = 640,
                Height = 360,
                Content = new TextBlock { Text = "Harness evaluation" },
            };
            window.Show();

            InboundRenderedFrame frame = AvaloniaInboundMcpUiBridge.RenderOwnedFrame(window);

            Assert.Equal("image/png", frame.MediaType);
            Assert.False(frame.IsTruncated);
            Assert.Null(frame.Error);
            Assert.NotEmpty(frame.Sha256);
            Assert.True(Convert.FromBase64String(frame.Base64).Length > 100);
            window.Close();
        }, CancellationToken.None);
    }
}
