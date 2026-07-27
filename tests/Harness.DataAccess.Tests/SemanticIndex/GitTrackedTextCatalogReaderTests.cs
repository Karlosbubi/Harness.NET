using Harness.DataAccess.SemanticIndex;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.SemanticIndex;

public sealed class GitTrackedTextCatalogReaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"harness-index-catalog-{Guid.NewGuid():N}");

    [Fact]
    public async Task Reads_only_eligible_non_sensitive_tracked_utf8_text()
    {
        Repository.Init(root);
        Write("src/Program.cs", "namespace Example;\npublic static class Program { }\n");
        Write("README.md", "# Example\nUseful documentation.\n");
        Write("src/Example.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("config/settings.yaml", "feature: enabled");
        Write("appsettings.json", "{\"password\":\"really-secret-value\"}");
        Write(".env", "OPENROUTER_API_KEY" + "=should-never-be-indexed");
        Write("src/Generated.g.cs", "// generated");
        WriteBytes("image.png", [0, 1, 2, 3]);
        Write("ignored.cs", "not tracked");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, [
                "src/Program.cs",
                "README.md",
                "src/Example.csproj",
                "config/settings.yaml",
                "appsettings.json",
                ".env",
                "src/Generated.g.cs",
                "image.png",
            ]);
        }

        GitTrackedTextCatalogReader reader = new();
        TrackedTextCatalog result = await reader.ReadAsync(root);

        Assert.Null(result.Error);
        Assert.Equal(8, result.TrackedFileCount);
        Assert.Equal(4, result.SkippedFileCount);
        Assert.Equal(
            ["README.md", "config/settings.yaml", "src/Example.csproj", "src/Program.cs"],
            result.Documents.Select(item => item.Path));
        Assert.All(result.Documents, item => Assert.Equal(64, item.ContentHash.Length));
        Assert.DoesNotContain(result.Documents, item => item.Content.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_a_nested_workspace_instead_of_indexing_the_parent_repository()
    {
        Repository.Init(root);
        string nested = Path.Combine(root, "src");
        Directory.CreateDirectory(nested);

        TrackedTextCatalog result = await new GitTrackedTextCatalogReader().ReadAsync(nested);

        Assert.Equal("repository_mismatch", result.ErrorCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteBytes(string relativePath, byte[] content)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
