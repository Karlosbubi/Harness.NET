using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class WorkspaceFileReaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-file-reader-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reads_utf8_text_and_bounds_large_content()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "large.txt");
        await File.WriteAllTextAsync(path, new string('a', 70 * 1024));
        WorkspaceFileReader reader = new();

        WorkspaceFileRead result = await reader.ReadAsync(root, "large.txt");

        Assert.Null(result.Error);
        Assert.Equal("large.txt", result.Path);
        Assert.Equal(64 * 1024, result.Content.Length);
        Assert.Equal(70 * 1024, result.SizeBytes);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task Rejects_paths_outside_the_workspace()
    {
        Directory.CreateDirectory(root);
        WorkspaceFileReader reader = new();

        WorkspaceFileRead result = await reader.ReadAsync(root, "../outside.txt");

        Assert.Equal("outside_workspace", result.ErrorCode);
        Assert.Empty(result.Content);
    }

    [Fact]
    public async Task Truncation_does_not_reject_a_split_utf8_character()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "unicode.txt");
        await File.WriteAllTextAsync(path, $"{new string('a', (64 * 1024) - 1)}€");
        WorkspaceFileReader reader = new();

        WorkspaceFileRead result = await reader.ReadAsync(root, "unicode.txt");

        Assert.Null(result.Error);
        Assert.True(result.IsTruncated);
        Assert.Equal((64 * 1024) - 1, result.Content.Length);
    }

    [Fact]
    public async Task Rejects_symbolic_link_hops()
    {
        Directory.CreateDirectory(root);
        string outside = Path.Combine(Path.GetDirectoryName(root)!, $"outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "secret");
        Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
        WorkspaceFileReader reader = new();

        WorkspaceFileRead result = await reader.ReadAsync(root, "linked/secret.txt");

        Assert.Equal("symlink_not_allowed", result.ErrorCode);
        Assert.Empty(result.Content);
        Directory.Delete(outside, recursive: true);
    }

    [Fact]
    public async Task Rejects_non_utf8_content()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, "binary.dat"), [0xff, 0xfe, 0xfd]);
        WorkspaceFileReader reader = new();

        WorkspaceFileRead result = await reader.ReadAsync(root, "binary.dat");

        Assert.Equal("not_text", result.ErrorCode);
        Assert.Empty(result.Content);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
