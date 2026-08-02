using Harness.DataAccess.Commits;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Commits;

public sealed class LibGitGoalCommitterTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-commit-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Commits_the_exact_reviewed_tracked_and_untracked_diff_and_reconciles_retry()
    {
        (string branch, string initialSha) = CreateRepository();
        File.AppendAllText(Path.Combine(root, "tracked.txt"), "changed\n");
        File.WriteAllText(Path.Combine(root, "new.txt"), "new content\n");
        LibGitGoalCommitter committer = new();
        GoalCommitInspection inspection = await committer.InspectAsync(new(
            new(root), new(branch)));
        DateTimeOffset approvedAt = DateTimeOffset.Parse("2026-07-28T20:00:00Z");
        string message = "Implement reviewed change\n\n" +
            $"Harness-Diff-SHA256: {inspection.DiffSha256!.Value}";
        GoalCommitRequest request = new(
            new(root), new(branch), new(initialSha), inspection.DiffSha256,
            new(message), new("Harness User"), new("user@example.test"), approvedAt);

        GoalCommitResult committed = await committer.CommitAsync(request);
        GoalCommitResult reconciled = await committer.CommitAsync(request);

        Assert.Null(committed.Error);
        Assert.False(committed.WasReconciled);
        Assert.Equal(committed.CommitSha, reconciled.CommitSha);
        Assert.True(reconciled.WasReconciled);
        Assert.Contains("new.txt", inspection.Diff.Value, StringComparison.Ordinal);
        Assert.Contains("new content", inspection.Diff.Value, StringComparison.Ordinal);
        using Repository repository = new(root);
        Assert.Empty(repository.RetrieveStatus());
        Assert.Equal(initialSha, repository.Head.Tip?.Parents.Single().Sha);
        Assert.Contains(inspection.DiffSha256.Value, repository.Head.Tip?.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_diff_that_changed_after_review()
    {
        (string branch, string initialSha) = CreateRepository();
        File.AppendAllText(Path.Combine(root, "tracked.txt"), "reviewed\n");
        LibGitGoalCommitter committer = new();
        GoalCommitInspection inspection = await committer.InspectAsync(new(
            new(root), new(branch)));
        File.AppendAllText(Path.Combine(root, "tracked.txt"), "not reviewed\n");
        string message = "Commit\n\n" +
            $"Harness-Diff-SHA256: {inspection.DiffSha256!.Value}";

        GoalCommitResult result = await committer.CommitAsync(new(
            new(root), new(branch), new(initialSha), inspection.DiffSha256,
            new(message), new("Harness User"), new("user@example.test"),
            DateTimeOffset.UtcNow));

        Assert.Equal("diff_changed", result.ErrorCode);
        using Repository repository = new(root);
        Assert.Equal(initialSha, repository.Head.Tip?.Sha);
    }

    [Fact]
    public async Task Conflict_is_blocked_until_the_user_resolves_and_stages_it()
    {
        (string branch, _) = CreateRepository();
        Signature signature = new("Test", "test@example.test", DateTimeOffset.UtcNow);
        using (Repository repository = new(root))
        {
            Branch incoming = repository.CreateBranch("incoming");
            Commands.Checkout(repository, incoming);
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "incoming change\n");
            Commands.Stage(repository, "tracked.txt");
            repository.Commit("incoming", signature, signature);

            Commands.Checkout(repository, branch);
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "goal change\n");
            Commands.Stage(repository, "tracked.txt");
            repository.Commit("goal", signature, signature);
            MergeResult merge = repository.Merge(incoming, signature);
            Assert.Equal(MergeStatus.Conflicts, merge.Status);
        }

        LibGitGoalCommitter committer = new();
        GoalCommitInspection conflicted = await committer.InspectAsync(new(
            new(root), new(branch)));

        Assert.Equal("conflicts_present", conflicted.ErrorCode);
        Assert.Contains("Resolve all Git conflicts", conflicted.Error,
            StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(root, "tracked.txt"), "resolved goal and incoming\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "tracked.txt");
        }

        GoalCommitInspection resolved = await committer.InspectAsync(new(
            new(root), new(branch)));

        Assert.Null(resolved.Error);
        Assert.NotNull(resolved.DiffSha256);
        Assert.Contains("resolved goal and incoming", resolved.Diff.Value,
            StringComparison.Ordinal);
    }

    private (string Branch, string InitialSha) CreateRepository()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        using Repository repository = new(root);
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "initial\n");
        Commands.Stage(repository, "tracked.txt");
        Signature signature = new("Test", "test@example.test", DateTimeOffset.UtcNow);
        Commit initial = repository.Commit("initial", signature, signature);
        return (repository.Head.FriendlyName, initial.Sha);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
