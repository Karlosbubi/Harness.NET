using Harness.DataAccess.Configuration;
using Harness.DataAccess.Framework;

namespace Harness.DataAccess.Tests.Framework;

public sealed class FileFrameworkSourceReaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-framework-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Loads_global_and_repository_guidance_with_provenance()
    {
        ApplicationPaths paths = CreatePaths();
        Directory.CreateDirectory(paths.ConfigDirectory);
        string workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(
            Path.Combine(paths.ConfigDirectory, "framework.md"),
            "# Private preferences");
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "AGENTS.md"),
            "# Repository guidance");
        FileFrameworkSourceReader reader = new(new StubApplicationPaths(paths));

        FrameworkSourceResult result = await reader.ReadAsync(workspace);

        Assert.Empty(result.Errors);
        Assert.Collection(
            result.Documents,
            global =>
            {
                Assert.Equal("global", global.Layer);
                Assert.Equal(0, global.Precedence);
                Assert.True(global.IsPrivate);
                Assert.Contains("Private preferences", global.Content, StringComparison.Ordinal);
            },
            repository =>
            {
                Assert.Equal("repository", repository.Layer);
                Assert.Equal(1, repository.Precedence);
                Assert.False(repository.IsPrivate);
                Assert.EndsWith("AGENTS.md", repository.Source, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Missing_optional_documents_produce_an_empty_result()
    {
        ApplicationPaths paths = CreatePaths();
        string workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        FileFrameworkSourceReader reader = new(new StubApplicationPaths(paths));

        FrameworkSourceResult result = await reader.ReadAsync(workspace);

        Assert.Empty(result.Documents);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Oversized_document_is_rejected_without_loading_content()
    {
        ApplicationPaths paths = CreatePaths();
        string workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllBytesAsync(
            Path.Combine(workspace, "AGENTS.md"),
            new byte[1024 * 1024 + 1]);
        FileFrameworkSourceReader reader = new(new StubApplicationPaths(paths));

        FrameworkSourceResult result = await reader.ReadAsync(workspace);

        Assert.Empty(result.Documents);
        Assert.Contains(result.Errors, error => error.Contains("exceeds 1 MiB", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(root, "config"),
        Path.Combine(root, "data"),
        Path.Combine(root, "state"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
