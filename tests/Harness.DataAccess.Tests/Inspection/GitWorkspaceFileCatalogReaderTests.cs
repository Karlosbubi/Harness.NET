using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class GitWorkspaceFileCatalogReaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-file-catalog-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Lists_only_confined_tracked_files_in_repository_order()
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Repository.Init(root);
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "read me");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "App.cs"), "class App;");
        await File.WriteAllTextAsync(Path.Combine(root, "untracked.txt"), "not listed");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "README.md");
            Commands.Stage(repository, "src/App.cs");
        }

        WorkspaceFileCatalog result = await new GitWorkspaceFileCatalogReader().ReadAsync(root);

        Assert.Null(result.Error);
        Assert.Equal(["README.md", "src/App.cs"], result.Files.Select(file => file.Value));
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task Rejects_a_non_repository_directory()
    {
        Directory.CreateDirectory(root);

        WorkspaceFileCatalog result = await new GitWorkspaceFileCatalogReader().ReadAsync(root);

        Assert.Equal("repository_missing", result.ErrorCode);
        Assert.Empty(result.Files);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
