using Harness.DataAccess.Configuration;
using Harness.DataAccess.Worktrees;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Worktrees;

public sealed class GitGoalWorktreeManagerTests : IDisposable
{
    private const string GoalId = "0123456789abcdef0123456789abcdef";
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-worktree-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Creates_an_idempotent_goal_worktree_without_touching_dirty_user_state()
    {
        string repositoryRoot = Path.Combine(root, "repository");
        Directory.CreateDirectory(repositoryRoot);
        Repository.Init(repositoryRoot);
        string trackedFile = Path.Combine(repositoryRoot, "tracked.txt");
        await File.WriteAllTextAsync(trackedFile, "base\n");
        string baseCommit;
        using (Repository repository = new(repositoryRoot))
        {
            Commands.Stage(repository, "tracked.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            baseCommit = repository.Commit("initial", signature, signature).Sha;
        }
        await File.WriteAllTextAsync(trackedFile, "dirty user change\n");
        GitGoalWorktreeManager manager = new(new StubApplicationPaths(CreatePaths()));

        GoalWorktreeResult first = await manager.CreateAsync(GoalId, repositoryRoot);
        GoalWorktreeResult second = await manager.CreateAsync(GoalId, repositoryRoot);

        Assert.Null(first.Error);
        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal("harness/goal-0123456789ab", first.Branch);
        Assert.Equal(baseCommit, first.BaseCommit);
        Assert.Equal(first.Path, second.Path);
        using Repository original = new(repositoryRoot);
        using Repository worktree = new(first.Path);
        Assert.True(original.RetrieveStatus().IsDirty);
        Assert.Equal("harness/goal-0123456789ab", worktree.Head.FriendlyName);
        Assert.Equal(baseCommit, worktree.Head.Tip.Sha);
        Assert.Equal("base\n", await File.ReadAllTextAsync(Path.Combine(first.Path, "tracked.txt")));
    }

    [Fact]
    public async Task Rejects_noncanonical_goal_identifiers_before_git_execution()
    {
        GitGoalWorktreeManager manager = new(new StubApplicationPaths(CreatePaths()));

        GoalWorktreeResult result = await manager.CreateAsync("../goal", Path.Combine(root, "missing"));

        Assert.Equal("invalid_goal", result.ErrorCode);
        Assert.Empty(result.Path);
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
