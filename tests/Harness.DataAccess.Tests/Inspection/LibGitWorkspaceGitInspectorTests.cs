using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class LibGitWorkspaceGitInspectorTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-git-inspection-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Returns_branch_head_status_and_combined_diff()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        string file = Path.Combine(root, "tracked.txt");
        await File.WriteAllTextAsync(file, "before\n");
        string headSha;
        string branch;
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "tracked.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            Commit commit = repository.Commit("initial", signature, signature);
            headSha = commit.Sha;
            branch = repository.Head.FriendlyName;
        }
        await File.WriteAllTextAsync(file, "after\n");

        WorkspaceGitState result = await new LibGitWorkspaceGitInspector()
            .InspectAsync(root);

        Assert.Null(result.Error);
        Assert.Equal(branch, result.Branch);
        Assert.Equal(headSha, result.HeadSha);
        WorkspaceGitFileChange change = Assert.Single(result.Changes);
        Assert.Equal("tracked.txt", change.Path);
        Assert.Contains("ModifiedInWorkdir", change.Status, StringComparison.Ordinal);
        Assert.False(change.IsStaged);
        Assert.True(change.IsUnstaged);
        Assert.NotEmpty(result.Fingerprint);
        Assert.Empty(result.StagedDiff);
        Assert.Contains("+after", result.UnstagedDiff, StringComparison.Ordinal);
        string patchMetadata = System.Text.Json.JsonSerializer.Serialize(result.PatchUnits);
        Assert.DoesNotContain("\"Patch\"", patchMetadata, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyInReverse", patchMetadata, StringComparison.Ordinal);
        Assert.Contains("-before", result.Diff, StringComparison.Ordinal);
        Assert.Contains("+after", result.Diff, StringComparison.Ordinal);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task Separates_staged_and_unstaged_state()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        string file = Path.Combine(root, "tracked.txt");
        await File.WriteAllTextAsync(file, "before\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "tracked.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("initial", signature, signature);
        }
        await File.WriteAllTextAsync(file, "staged\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "tracked.txt");
        await File.WriteAllTextAsync(file, "unstaged\n");

        WorkspaceGitState result = await new LibGitWorkspaceGitInspector().InspectAsync(root);

        WorkspaceGitFileChange change = Assert.Single(result.Changes);
        Assert.True(change.IsStaged);
        Assert.True(change.IsUnstaged);
        Assert.Contains("+staged", result.StagedDiff, StringComparison.Ordinal);
        Assert.Contains("+unstaged", result.UnstagedDiff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bounds_large_diff_content()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        string file = Path.Combine(root, "tracked.txt");
        await File.WriteAllTextAsync(file, "before\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "tracked.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("initial", signature, signature);
        }
        await File.WriteAllTextAsync(file, new string('x', 200 * 1024));

        WorkspaceGitState result = await new LibGitWorkspaceGitInspector()
            .InspectAsync(root);

        Assert.Null(result.Error);
        Assert.True(result.IsTruncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.Diff) <= 128 * 1024);
        Assert.Empty(result.PatchUnits!);
    }

    [Fact]
    public async Task Excludes_untracked_file_content_from_diff()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        string tracked = Path.Combine(root, "tracked.txt");
        await File.WriteAllTextAsync(tracked, "before\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "tracked.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("initial", signature, signature);
        }
        await File.WriteAllTextAsync(tracked, "after\n");
        await File.WriteAllTextAsync(
            Path.Combine(root, "local-secret.txt"),
            "credential-that-must-not-enter-a-diff");

        WorkspaceGitState result = await new LibGitWorkspaceGitInspector()
            .InspectAsync(root);

        Assert.Contains(result.Changes, change => change.Path == "local-secret.txt");
        Assert.Contains("+after", result.Diff, StringComparison.Ordinal);
        Assert.DoesNotContain("local-secret.txt", result.Diff, StringComparison.Ordinal);
        Assert.DoesNotContain("credential-that-must-not-enter-a-diff", result.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_nested_path_as_the_workspace_root()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        string nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;

        WorkspaceGitState result = await new LibGitWorkspaceGitInspector()
            .InspectAsync(nested);

        Assert.Equal("repository_mismatch", result.ErrorCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
