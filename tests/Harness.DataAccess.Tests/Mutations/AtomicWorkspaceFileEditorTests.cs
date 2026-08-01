using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.Mutations;

namespace Harness.DataAccess.Tests.Mutations;

public sealed class AtomicWorkspaceFileEditorTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-file-edit-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Replaces_expected_content_and_returns_hash_evidence()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Program.cs");
        await File.WriteAllTextAsync(path, "before");
        string expectedHash = Hash("before");
        AtomicWorkspaceFileEditor editor = new();

        WorkspaceFileEditResult result = await editor.ApplyAsync(
            root,
            new("Program.cs", expectedHash, "after"));

        Assert.Null(result.Error);
        Assert.Equal(expectedHash, result.PreviousSha256);
        Assert.Equal(Hash("after"), result.NewSha256);
        Assert.Equal(5, result.BytesWritten);
        Assert.False(result.WasCreated);
        Assert.Equal("after", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Rejects_stale_content_without_overwriting_the_file()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Program.cs");
        await File.WriteAllTextAsync(path, "current");
        AtomicWorkspaceFileEditor editor = new();

        WorkspaceFileEditResult result = await editor.ApplyAsync(
            root,
            new("Program.cs", Hash("stale"), "replacement"));

        Assert.Equal("content_changed", result.ErrorCode);
        Assert.Equal("current", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Creates_a_new_file_only_when_no_previous_hash_is_expected()
    {
        Directory.CreateDirectory(root);
        AtomicWorkspaceFileEditor editor = new();

        WorkspaceFileEditResult result = await editor.ApplyAsync(
            root,
            new("new.txt", ExpectedSha256: null, "content"));

        Assert.Null(result.Error);
        Assert.True(result.WasCreated);
        Assert.Equal("content", await File.ReadAllTextAsync(Path.Combine(root, "new.txt")));
    }

    [Fact]
    public async Task Rejects_paths_outside_the_worktree()
    {
        Directory.CreateDirectory(root);
        AtomicWorkspaceFileEditor editor = new();

        WorkspaceFileEditResult result = await editor.ApplyAsync(
            root,
            new("../outside.txt", null, "content"));

        Assert.Equal("outside_workspace", result.ErrorCode);
    }

    [Fact]
    public async Task Applies_multiple_baseline_protected_files_as_one_batch()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "First.cs"), "first");
        await File.WriteAllTextAsync(Path.Combine(root, "Second.cs"), "second");
        AtomicWorkspaceFileEditor editor = new();

        WorkspaceFileBatchEditResult result = await editor.ApplyBatchAsync(
            root,
            new([
                new("First.cs", Hash("first"), "renamed first"),
                new("Second.cs", Hash("second"), "renamed second"),
            ]));

        Assert.Null(result.ErrorCode);
        Assert.False(result.WasRolledBack);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal("renamed first", await File.ReadAllTextAsync(Path.Combine(root, "First.cs")));
        Assert.Equal("renamed second", await File.ReadAllTextAsync(Path.Combine(root, "Second.cs")));
        Assert.Empty(Directory.EnumerateFiles(root, ".harness-*.tmp"));
    }

    [Fact]
    public async Task Rejects_the_whole_batch_when_any_baseline_is_stale()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "First.cs"), "first");
        await File.WriteAllTextAsync(Path.Combine(root, "Second.cs"), "second");
        AtomicWorkspaceFileEditor editor = new();

        WorkspaceFileBatchEditResult result = await editor.ApplyBatchAsync(
            root,
            new([
                new("First.cs", Hash("first"), "changed first"),
                new("Second.cs", Hash("stale"), "changed second"),
            ]));

        Assert.Equal("content_changed", result.ErrorCode);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(root, "First.cs")));
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(root, "Second.cs")));
    }

    [Fact]
    public async Task Rolls_back_every_file_when_a_commit_fails_mid_batch()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "First.cs"), "first");
        await File.WriteAllTextAsync(Path.Combine(root, "Second.cs"), "second");
        AtomicWorkspaceFileEditor editor = new(index =>
            index == 1 ? new IOException("Injected failure.") : null);

        WorkspaceFileBatchEditResult result = await editor.ApplyBatchAsync(
            root,
            new([
                new("First.cs", Hash("first"), "changed first"),
                new("Second.cs", Hash("second"), "changed second"),
            ]));

        Assert.Equal("batch_write_failed", result.ErrorCode);
        Assert.True(result.WasRolledBack);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(root, "First.cs")));
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(root, "Second.cs")));
        Assert.Empty(Directory.EnumerateFiles(root, ".harness-*.tmp"));
    }

    [Fact]
    public async Task Cancellation_before_commit_leaves_every_file_unchanged()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "First.cs"), "first");
        using CancellationTokenSource cancellation = new();
        AtomicWorkspaceFileEditor editor = new(_ =>
        {
            cancellation.Cancel();
            return new OperationCanceledException(cancellation.Token);
        });

        WorkspaceFileBatchEditResult result = await editor.ApplyBatchAsync(
            root,
            new([new("First.cs", Hash("first"), "changed")]),
            cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.True(result.WasRolledBack);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(root, "First.cs")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Hash(string content) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
