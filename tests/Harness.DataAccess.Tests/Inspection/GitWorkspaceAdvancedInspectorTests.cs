using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class GitWorkspaceAdvancedInspectorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "harness-advanced-inspection",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Tree_is_git_tracked_bounded_and_paged()
    {
        Initialize(("src/A.cs", "class A { }\n"), ("src/B.cs", "class B { }\n"),
            ("README.md", "read me\n"));
        GitWorkspaceAdvancedInspector inspector = new();

        WorkspaceTreeResult first = await inspector.ListTreeAsync(root,
            new(new("src"), new("*.cs"), 4, 1, null));
        WorkspaceTreeResult second = await inspector.ListTreeAsync(root,
            new(new("src"), new("*.cs"), 4, 1, first.Continuation));

        Assert.Single(first.Entries);
        Assert.True(first.IsTruncated);
        Assert.NotNull(first.Continuation);
        Assert.Single(second.Entries);
        Assert.NotEqual(first.Entries[0].Path, second.Entries[0].Path);
    }

    [Fact]
    public async Task Range_returns_coordinates_hash_and_rejects_untracked_files()
    {
        Initialize(("src/A.cs", "one\ntwo\nthree\n"));
        await File.WriteAllTextAsync(Path.Combine(root, "untracked.txt"), "secret");
        GitWorkspaceAdvancedInspector inspector = new();

        WorkspaceRangeResult range = await inspector.ReadRangeAsync(root,
            new(new("src/A.cs"), 2, 1));
        WorkspaceRangeResult denied = await inspector.ReadRangeAsync(root,
            new(new("untracked.txt"), 1, 1));

        Assert.Equal("two", range.Content);
        Assert.Equal(2, range.StartLine);
        Assert.Equal(2, range.EndLine);
        Assert.NotNull(range.Sha256);
        Assert.True(range.IsTruncated);
        Assert.Equal("file_untracked", denied.ErrorCode);
    }

    [Fact]
    public async Task Regex_returns_one_based_coordinates_and_continuation()
    {
        Initialize(("src/A.cs", "alpha beta\nalpha\n"), ("src/B.cs", "alpha\n"));
        GitWorkspaceAdvancedInspector inspector = new();

        WorkspaceRegexResult first = await inspector.SearchRegexAsync(root,
            new(new("alpha"), new("*.cs"), 1, null));
        WorkspaceRegexResult second = await inspector.SearchRegexAsync(root,
            new(new("alpha"), new("*.cs"), 1, first.Continuation));

        Assert.Single(first.Matches);
        Assert.Equal(1, first.Matches[0].Line);
        Assert.Equal(1, first.Matches[0].Character);
        Assert.True(first.IsTruncated);
        Assert.Single(second.Matches);
    }

    private void Initialize(params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        foreach ((string path, string content) in files)
        {
            string target = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
        using Repository repository = new(root);
        Commands.Stage(repository, files.Select(file => file.Path));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
