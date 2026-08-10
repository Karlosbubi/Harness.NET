using Harness.DataAccess.VisualCapture;

namespace Harness.DataAccess.Tests.VisualCapture;

public sealed class PortalFileImageSourceReaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"harness-portal-image-{Guid.NewGuid():N}");

    [Fact]
    public async Task Reads_exact_bounded_file_and_rejects_oversize_or_non_file_uri()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "frame.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        PortalFileImageSourceReader reader = new();

        PortalImageReadResult read = await reader.ReadAsync(new Uri(path), 4);
        PortalImageReadResult oversized = await reader.ReadAsync(new Uri(path), 3);
        PortalImageReadResult remote = await reader.ReadAsync(new Uri("https://example.test/frame.png"), 4);

        Assert.Equal([1, 2, 3, 4], read.Content.ToArray());
        Assert.Equal(PortalImageReadState.TooLarge, oversized.State);
        Assert.Equal(PortalImageReadState.InvalidUri, remote.State);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
