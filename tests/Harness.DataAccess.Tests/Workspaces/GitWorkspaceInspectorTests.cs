using Harness.DataAccess.Workspaces;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Workspaces;

public sealed class GitWorkspaceInspectorTests : IDisposable
{
    private readonly string repositoryDirectory = Path.Combine(
        Path.GetTempPath(),
        "harness-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Discovers_repository_from_nested_path_and_reports_tracked_dotnet_entries()
    {
        Directory.CreateDirectory(repositoryDirectory);
        Repository.Init(repositoryDirectory);
        string projectPath = Path.Combine(repositoryDirectory, "Sample.csproj");
        string ignoredPath = Path.Combine(repositoryDirectory, "notes.txt");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(ignoredPath, "not an entry point");
        using (Repository repository = new(repositoryDirectory))
        {
            Commands.Stage(repository, "Sample.csproj");
            Commands.Stage(repository, "notes.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("initial", signature, signature);
        }
        string nested = Directory.CreateDirectory(
            Path.Combine(repositoryDirectory, "src", "Nested")).FullName;
        File.AppendAllText(projectPath, Environment.NewLine);

        WorkspaceInspection inspection = await new GitWorkspaceInspector().InspectAsync(nested);

        Assert.Null(inspection.Error);
        Assert.Equal(Path.GetFullPath(repositoryDirectory), inspection.RootPath);
        Assert.True(inspection.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(inspection.Branch));
        Assert.Equal(Path.GetFullPath(projectPath), Assert.Single(inspection.EntryPoints));
    }

    [Fact]
    public async Task Returns_a_recorded_failure_for_non_repository_path()
    {
        Directory.CreateDirectory(repositoryDirectory);

        WorkspaceInspection inspection = await new GitWorkspaceInspector()
            .InspectAsync(repositoryDirectory);

        Assert.NotNull(inspection.Error);
        Assert.Empty(inspection.EntryPoints);
    }

    public void Dispose()
    {
        if (Directory.Exists(repositoryDirectory))
        {
            Directory.Delete(repositoryDirectory, recursive: true);
        }
    }
}
