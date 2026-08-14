using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class LibGitDeveloperGitRepositoryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-developer-git-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Stages_and_unstages_exact_path_from_expected_state()
    {
        await InitializeAsync();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        await File.WriteAllTextAsync(first, "changed first\n");
        await File.WriteAllTextAsync(second, "changed second\n");
        var inspector = new LibGitWorkspaceGitInspector();
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await inspector.InspectAsync(root);

        DeveloperGitIndexResult staged = await sut.UpdateIndexAsync(new(
            root,
            new(before.Fingerprint),
            DeveloperGitIndexOperation.Stage,
            [new("first.txt")]));

        Assert.Null(staged.Error);
        Assert.Equal("first.txt", Assert.Single(staged.AffectedPaths).Value);
        Assert.True(staged.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.False(staged.State.Changes.Single(change => change.Path == "second.txt").IsStaged);

        DeveloperGitIndexResult unstaged = await sut.UpdateIndexAsync(new(
            root,
            new(staged.State.Fingerprint),
            DeveloperGitIndexOperation.Unstage,
            [new("first.txt")]));

        Assert.Null(unstaged.Error);
        Assert.False(unstaged.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.True(unstaged.State.Changes.Single(change => change.Path == "first.txt").IsUnstaged);
    }

    [Fact]
    public async Task Rejects_stale_fingerprint_without_mutating_index()
    {
        await InitializeAsync();
        string first = Path.Combine(root, "first.txt");
        await File.WriteAllTextAsync(first, "first change\n");
        var inspector = new LibGitWorkspaceGitInspector();
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState displayed = await inspector.InspectAsync(root);
        await File.WriteAllTextAsync(first, "later change\n");

        DeveloperGitIndexResult result = await sut.UpdateIndexAsync(new(
            root,
            new(displayed.Fingerprint),
            DeveloperGitIndexOperation.Stage,
            [new("first.txt")]));

        Assert.Equal("git_state_stale", result.ErrorCode);
        Assert.NotNull(result.State);
        Assert.NotEqual(displayed.Fingerprint, result.State.Fingerprint);
        Assert.False(result.State.Changes.Single(change => change.Path == "first.txt").IsStaged);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(".git/config")]
    [InlineData("/absolute.txt")]
    public async Task Rejects_paths_outside_the_worktree(string path)
    {
        await InitializeAsync();

        DeveloperGitIndexResult result = await new LibGitDeveloperGitRepository().UpdateIndexAsync(new(
            root,
            new("irrelevant"),
            DeveloperGitIndexOperation.Stage,
            [new(path)]));

        Assert.Equal("git_paths_invalid", result.ErrorCode);
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "first\n");
        await File.WriteAllTextAsync(Path.Combine(root, "second.txt"), "second\n");
        using Repository repository = new(root);
        Commands.Stage(repository, "*");
        Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
        repository.Commit("initial", signature, signature);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
