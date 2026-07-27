using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class GitWorkspaceTextSearcherTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-search-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Searches_only_tracked_text_with_line_records()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "tracked.cs"),
            "first line\n// Needle is here\nlast line");
        await File.WriteAllTextAsync(Path.Combine(root, "untracked.cs"), "needle");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "tracked.cs");
        }

        WorkspaceTextSearch result = await new GitWorkspaceTextSearcher()
            .SearchAsync(root, "needle");

        Assert.Null(result.Error);
        WorkspaceTextMatch match = Assert.Single(result.Matches);
        Assert.Equal("tracked.cs", match.Path);
        Assert.Equal(2, match.LineNumber);
        Assert.Equal("// Needle is here", match.Text);
        Assert.Equal(1, result.FilesScanned);
    }

    [Fact]
    public async Task Bounds_match_count_and_reports_truncation()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        await File.WriteAllLinesAsync(
            Path.Combine(root, "many.txt"),
            Enumerable.Repeat("needle", 101));
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "many.txt");
        }

        WorkspaceTextSearch result = await new GitWorkspaceTextSearcher()
            .SearchAsync(root, "needle");

        Assert.Equal(100, result.Matches.Count);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task Rejects_invalid_queries_before_opening_a_repository()
    {
        WorkspaceTextSearch result = await new GitWorkspaceTextSearcher()
            .SearchAsync(root, string.Empty);

        Assert.Equal("invalid_query", result.ErrorCode);
        Assert.Empty(result.Matches);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
