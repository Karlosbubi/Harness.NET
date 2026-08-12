using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mcp;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Mcp;

public sealed class InboundMcpEvaluationFixtureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "harness-evaluation-fixture", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Ensure_and_reset_are_confined_to_disposable_fixture()
    {
        Directory.CreateDirectory(root);
        string outside = Path.Combine(root, "outside.txt");
        await File.WriteAllTextAsync(outside, "developer state");
        InboundMcpEvaluationFixture fixture = new(new Paths(root), TimeProvider.System);
        InboundMcpEvaluationSnapshot baseline = await fixture.EnsureAsync();
        string source = Path.Combine(baseline.RootPath, "src", "Fixture", "Counter.cs");
        await File.AppendAllTextAsync(source, "// changed\n");
        await File.WriteAllTextAsync(Path.Combine(baseline.RootPath, "untracked.txt"), "temporary");

        InboundMcpEvaluationSnapshot reset = await fixture.ResetAsync();

        Assert.Equal(baseline.Head, reset.Head);
        Assert.Equal(0, reset.ChangedFiles);
        Assert.DoesNotContain("changed", await File.ReadAllTextAsync(source));
        Assert.False(File.Exists(Path.Combine(baseline.RootPath, "untracked.txt")));
        Assert.Equal("developer state", await File.ReadAllTextAsync(outside));
    }

    [Fact]
    public async Task Ensure_uses_the_single_tracked_solution_in_a_preseeded_fixture()
    {
        string repositoryRoot = Path.Combine(root, "data", "evaluation-fixture");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "One"));
        await File.WriteAllTextAsync(
            Path.Combine(repositoryRoot, "Custom.slnx"),
            "<Solution><Project Path=\"src/One/One.csproj\" /></Solution>\n");
        await File.WriteAllTextAsync(
            Path.Combine(repositoryRoot, "src", "One", "One.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Repository.Init(repositoryRoot);
        using (Repository repository = new(repositoryRoot))
        {
            Commands.Stage(repository, "*");
            Signature signature = new("Test", "test@localhost", DateTimeOffset.UnixEpoch);
            repository.Commit("Seed custom fixture", signature, signature);
        }
        InboundMcpEvaluationFixture fixture = new(new Paths(root), TimeProvider.System);

        InboundMcpEvaluationSnapshot snapshot = await fixture.EnsureAsync();

        Assert.Equal(Path.Combine(repositoryRoot, "Custom.slnx"), snapshot.EntryPoint);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class Paths(string root) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = new(
            Path.Combine(root, "config"), Path.Combine(root, "data"),
            Path.Combine(root, "state"), Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
    }
}
