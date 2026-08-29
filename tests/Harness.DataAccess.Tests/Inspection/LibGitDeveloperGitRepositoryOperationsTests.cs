using Harness.DataAccess.Configuration;
using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed partial class LibGitDeveloperGitRepositoryTests
{
    [Fact]
    public async Task Worktree_create_and_remove_preserve_exact_branch_and_set_state()
    {
        await InitializeAsync();
        string path = NewWorktreePath();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitWorktreeInspection initial = await sut.InspectWorktreesAsync(root);
        DeveloperGitWorktree main = Assert.Single(initial.Worktrees);
        Assert.True(main.IsMain);

        DeveloperGitWorktreeResult created = await sut.ApplyWorktreeAsync(new(
            root, new(initial.State!.Fingerprint), initial.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Create, path, null, "feature-worktree", null, false));

        Assert.Null(created.Error);
        DeveloperGitWorktree linked = Assert.Single(created.Worktrees, item => !item.IsMain);
        Assert.Equal(Path.GetFullPath(path), linked.Path);
        Assert.Equal("feature-worktree", linked.Branch);
        Assert.False(linked.IsDirty);
        Assert.NotEqual(initial.WorktreeFingerprint, created.WorktreeFingerprint);

        DeveloperGitWorktreeResult removed = await sut.ApplyWorktreeAsync(new(
            root, new(created.State!.Fingerprint), created.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Remove, path, null, null,
            linked.StateFingerprint, false));

        Assert.Null(removed.Error);
        Assert.Single(removed.Worktrees);
        Assert.False(Directory.Exists(path));

        string reopenedPath = NewWorktreePath();
        DeveloperGitWorktreeResult reopened = await sut.ApplyWorktreeAsync(new(
            root, new(removed.State!.Fingerprint), removed.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Create, reopenedPath, "feature-worktree", null, null, false));
        Assert.Null(reopened.Error);
        Assert.Equal("feature-worktree", Assert.Single(reopened.Worktrees, item => !item.IsMain).Branch);
    }

    [Fact]
    public async Task Dirty_worktree_requires_exact_force_and_removes_only_selected_path()
    {
        await InitializeAsync();
        string path = NewWorktreePath();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitWorktreeInspection initial = await sut.InspectWorktreesAsync(root);
        DeveloperGitWorktreeResult created = await sut.ApplyWorktreeAsync(new(
            root, new(initial.State!.Fingerprint), initial.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Create, path, null, "dirty-worktree", null, false));
        await File.WriteAllTextAsync(Path.Combine(path, "untracked.txt"), "keep unless forced\n");
        DeveloperGitWorktreeInspection dirty = await sut.InspectWorktreesAsync(root);
        DeveloperGitWorktree selected = Assert.Single(dirty.Worktrees, item => !item.IsMain);
        Assert.True(selected.IsDirty);

        DeveloperGitWorktreeResult denied = await sut.ApplyWorktreeAsync(new(
            root, new(dirty.State!.Fingerprint), dirty.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Remove, path, null, null,
            selected.StateFingerprint, false));
        Assert.Equal("git_worktree_invalid", denied.ErrorCode);
        Assert.True(File.Exists(Path.Combine(path, "untracked.txt")));

        DeveloperGitWorktreeResult removed = await sut.ApplyWorktreeAsync(new(
            root, new(dirty.State.Fingerprint), dirty.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Remove, path, null, null,
            selected.StateFingerprint, true));
        Assert.Null(removed.Error);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task Worktree_operation_rejects_stale_linked_set_without_creating_target()
    {
        await InitializeAsync();
        string first = NewWorktreePath();
        string staleTarget = NewWorktreePath();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitWorktreeInspection displayed = await sut.InspectWorktreesAsync(root);
        DeveloperGitWorktreeResult changed = await sut.ApplyWorktreeAsync(new(
            root, new(displayed.State!.Fingerprint), displayed.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Create, first, null, "first-worktree", null, false));
        Assert.Null(changed.Error);

        DeveloperGitWorktreeResult stale = await sut.ApplyWorktreeAsync(new(
            root, new(displayed.State.Fingerprint), displayed.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Create, staleTarget, null, "stale-worktree", null, false));

        Assert.Equal("git_state_stale", stale.ErrorCode);
        Assert.False(Directory.Exists(staleTarget));
    }

    [Fact]
    public async Task Harness_managed_worktree_is_identified_and_cannot_be_removed()
    {
        await InitializeAsync();
        string managedRoot = root + "-managed";
        string path = Path.Combine(managedRoot, "goal-worktree");
        Directory.CreateDirectory(managedRoot);
        linkedWorktreePaths.Add(managedRoot);
        var paths = new StubApplicationPaths(new(
            managedRoot, managedRoot, managedRoot, managedRoot,
            Path.Combine(managedRoot, "state.db"), managedRoot, managedRoot));
        var sut = new LibGitDeveloperGitRepository(paths);
        DeveloperGitWorktreeInspection initial = await sut.InspectWorktreesAsync(root);
        DeveloperGitWorktreeResult created = await sut.ApplyWorktreeAsync(new(
            root, new(initial.State!.Fingerprint), initial.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Create, path, null, "managed-worktree", null, false));
        DeveloperGitWorktree managed = Assert.Single(created.Worktrees, item => !item.IsMain);
        Assert.True(managed.IsHarnessManaged);

        DeveloperGitWorktreeResult denied = await sut.ApplyWorktreeAsync(new(
            root, new(created.State!.Fingerprint), created.WorktreeFingerprint!,
            DeveloperGitWorktreeOperation.Remove, path, null, null, managed.StateFingerprint, true));

        Assert.Equal("git_worktree_invalid", denied.ErrorCode);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task Creates_and_applies_exact_stash_while_preserving_it()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "stashed first\n");
        await File.WriteAllTextAsync(Path.Combine(root, "untracked.txt"), "stashed untracked\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "first.txt");
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitStashInspection before = await sut.InspectStashesAsync(root);

        DeveloperGitStashResult created = await sut.ApplyStashAsync(new(
            root, new(before.State!.Fingerprint), DeveloperGitStashOperation.Create,
            null, "checkpoint", IncludeUntracked: true));

        Assert.Null(created.Error);
        DeveloperGitStash stash = Assert.Single(created.Stashes);
        Assert.Contains("checkpoint", stash.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "untracked.txt")));
        Assert.Empty(created.State!.Changes);

        DeveloperGitStashResult applied = await sut.ApplyStashAsync(new(
            root, new(created.State.Fingerprint), DeveloperGitStashOperation.Apply,
            stash.CommitSha, null, IncludeUntracked: false));

        Assert.Null(applied.Error);
        Assert.Equal(stash.CommitSha, applied.AppliedStashCommitSha);
        Assert.Single(applied.Stashes);
        Assert.True(applied.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.True(File.Exists(Path.Combine(root, "untracked.txt")));
    }

    [Fact]
    public async Task Stash_without_untracked_keeps_untracked_file_in_worktree()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "tracked change\n");
        await File.WriteAllTextAsync(Path.Combine(root, "untracked.txt"), "keep here\n");
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitStashInspection before = await sut.InspectStashesAsync(root);

        DeveloperGitStashResult result = await sut.ApplyStashAsync(new(
            root, new(before.State!.Fingerprint), DeveloperGitStashOperation.Create,
            null, "tracked only", IncludeUntracked: false));

        Assert.Null(result.Error);
        Assert.True(File.Exists(Path.Combine(root, "untracked.txt")));
        Assert.Single(result.State!.Changes, change => change.Path == "untracked.txt");
    }

    [Fact]
    public async Task Drops_exact_stash_commit_after_selectors_shift()
    {
        await InitializeAsync();
        var sut = new LibGitDeveloperGitRepository();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "first stash\n");
        DeveloperGitStashInspection initial = await sut.InspectStashesAsync(root);
        DeveloperGitStashResult first = await sut.ApplyStashAsync(new(
            root, new(initial.State!.Fingerprint), DeveloperGitStashOperation.Create,
            null, "first checkpoint", false));
        string firstSha = Assert.Single(first.Stashes).CommitSha;
        await File.WriteAllTextAsync(Path.Combine(root, "second.txt"), "second stash\n");
        DeveloperGitStashInspection secondDisplay = await sut.InspectStashesAsync(root);
        DeveloperGitStashResult second = await sut.ApplyStashAsync(new(
            root, new(secondDisplay.State!.Fingerprint), DeveloperGitStashOperation.Create,
            null, "second checkpoint", false));
        Assert.Equal(2, second.Stashes.Count);

        DeveloperGitStashResult dropped = await sut.ApplyStashAsync(new(
            root, new(second.State!.Fingerprint), DeveloperGitStashOperation.Drop,
            firstSha, null, false));

        Assert.Null(dropped.Error);
        DeveloperGitStash remaining = Assert.Single(dropped.Stashes);
        Assert.Contains("second checkpoint", remaining.Message, StringComparison.Ordinal);
        Assert.NotEqual(firstSha, remaining.CommitSha);
    }

    [Fact]
    public async Task Stash_operation_rejects_changed_worktree_before_mutation()
    {
        await InitializeAsync();
        var sut = new LibGitDeveloperGitRepository();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "stash me\n");
        DeveloperGitStashInspection before = await sut.InspectStashesAsync(root);
        DeveloperGitStashResult created = await sut.ApplyStashAsync(new(
            root, new(before.State!.Fingerprint), DeveloperGitStashOperation.Create,
            null, "checkpoint", false));
        DeveloperGitStash stash = Assert.Single(created.Stashes);
        DeveloperGitStashInspection displayed = await sut.InspectStashesAsync(root);
        await File.WriteAllTextAsync(Path.Combine(root, "second.txt"), "later change\n");

        DeveloperGitStashResult stale = await sut.ApplyStashAsync(new(
            root, new(displayed.State!.Fingerprint), DeveloperGitStashOperation.Apply,
            stash.CommitSha, null, false));

        Assert.Equal("git_state_stale", stale.ErrorCode);
        Assert.Single(stale.Stashes);
        Assert.Equal("later change\n", await File.ReadAllTextAsync(Path.Combine(root, "second.txt")));
    }

    [Fact]
    public async Task Conflicting_stash_apply_keeps_stash_and_reports_worktree_state()
    {
        await InitializeAsync();
        var sut = new LibGitDeveloperGitRepository();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "stashed version\n");
        DeveloperGitStashInspection initial = await sut.InspectStashesAsync(root);
        DeveloperGitStashResult created = await sut.ApplyStashAsync(new(
            root, new(initial.State!.Fingerprint), DeveloperGitStashOperation.Create,
            null, "conflicting checkpoint", false));
        DeveloperGitStash stash = Assert.Single(created.Stashes);
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "committed version\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "first.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("conflicting current commit", signature, signature);
        }
        DeveloperGitStashInspection displayed = await sut.InspectStashesAsync(root);

        DeveloperGitStashResult result = await sut.ApplyStashAsync(new(
            root, new(displayed.State!.Fingerprint), DeveloperGitStashOperation.Apply,
            stash.CommitSha, null, false));

        Assert.Equal("git_stash_apply_conflict", result.ErrorCode);
        Assert.Single(result.Stashes);
        Assert.True(result.State!.Changes.Single(change => change.Path == "first.txt").IsConflicted);
    }

    [Fact]
    public async Task Inspects_three_way_conflict_and_finds_unresolved_result_regions()
    {
        await CreateConflictAsync();
        var sut = new LibGitDeveloperGitRepository();

        DeveloperGitConflictInspection conflicts = await sut.InspectConflictsAsync(root);
        DeveloperGitConflictDocumentResult result = await sut.InspectConflictAsync(
            root, new("first.txt"));

        DeveloperGitConflictSummary summary = Assert.Single(conflicts.Conflicts);
        Assert.Equal("first.txt", summary.Path.Value);
        Assert.NotNull(summary.BaseBlob);
        Assert.NotNull(summary.OursBlob);
        Assert.NotNull(summary.TheirsBlob);
        Assert.False(summary.IsBinary);
        DeveloperGitConflictDocument document = Assert.IsType<DeveloperGitConflictDocument>(
            result.Document);
        Assert.Equal("first\n", document.Base.Text);
        Assert.Equal("main version\n", document.Ours.Text);
        Assert.Equal("branch version\n", document.Theirs.Text);
        Assert.Contains("<<<<<<<", document.Result, StringComparison.Ordinal);
        DeveloperGitConflictRegion region = Assert.Single(document.UnresolvedRegions);
        Assert.True(region.IsComplete);
        Assert.Equal(1, region.StartLine);
        Assert.NotNull(region.SeparatorLine);
        Assert.NotNull(region.EndLine);
        Assert.Equal(64, document.ResultHash.Value.Length);
        Assert.True(result.State!.Changes.Single(change => change.Path == "first.txt").IsConflicted);
    }

    [Fact]
    public async Task Saves_exact_conflict_result_without_resolving_until_separately_staged()
    {
        await CreateConflictAsync();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitConflictDocumentResult displayed = await sut.InspectConflictAsync(
            root, new("first.txt"));
        DeveloperGitConflictDocument document = Assert.IsType<DeveloperGitConflictDocument>(
            displayed.Document);

        DeveloperGitConflictDocumentResult saved = await sut.SaveConflictResultAsync(new(
            root,
            new(displayed.State!.Fingerprint),
            new("first.txt"),
            document.ResultHash,
            "resolved version\n"));

        Assert.Null(saved.Error);
        Assert.Equal("resolved version\n", saved.Document!.Result);
        Assert.Empty(saved.Document.UnresolvedRegions);
        Assert.True(saved.State!.Changes.Single(change => change.Path == "first.txt").IsConflicted);
        DeveloperGitIndexResult staged = await sut.StageConflictResultAsync(new(
            root,
            new(saved.State.Fingerprint),
            new("first.txt"),
            saved.Document.ResultHash));
        Assert.Null(staged.Error);
        Assert.DoesNotContain(staged.State!.Changes, change => change.Path == "first.txt" &&
            change.IsConflicted);
        Assert.Equal("resolved version\n", ReadIndexText("first.txt"));
    }

    [Fact]
    public async Task Refuses_to_stage_a_saved_result_while_conflict_markers_remain()
    {
        await CreateConflictAsync();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitConflictDocumentResult displayed = await sut.InspectConflictAsync(
            root, new("first.txt"));

        DeveloperGitIndexResult result = await sut.StageConflictResultAsync(new(
            root,
            new(displayed.State!.Fingerprint),
            new("first.txt"),
            displayed.Document!.ResultHash));

        Assert.Equal("git_conflict_markers_remain", result.ErrorCode);
        Assert.True(result.State!.Changes.Single(change => change.Path == "first.txt").IsConflicted);
    }

    [Fact]
    public async Task Rejects_stale_conflict_result_without_overwriting_newer_content()
    {
        await CreateConflictAsync();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitConflictDocumentResult displayed = await sut.InspectConflictAsync(
            root, new("first.txt"));
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "newer manual edit\n");

        DeveloperGitConflictDocumentResult result = await sut.SaveConflictResultAsync(new(
            root,
            new(displayed.State!.Fingerprint),
            new("first.txt"),
            displayed.Document!.ResultHash,
            "stale replacement\n"));

        Assert.Equal("git_state_stale", result.ErrorCode);
        Assert.Equal("newer manual edit\n", await File.ReadAllTextAsync(
            Path.Combine(root, "first.txt")));
    }

    [Fact]
    public async Task Pages_history_and_file_timeline_with_exact_commit_cursor()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "changed\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "first.txt");
            Signature signature = new("History Author", "history@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("change first", signature, signature);
        }
        var sut = new LibGitDeveloperGitRepository();

        DeveloperGitHistoryPage first = await sut.InspectHistoryAsync(new(
            root, new("first.txt"), null, MaximumResults: 1));
        DeveloperGitHistoryPage second = await sut.InspectHistoryAsync(new(
            root, new("first.txt"), first.NextCursor, MaximumResults: 1));

        Assert.Null(first.Error);
        Assert.Equal("change first", Assert.Single(first.Commits).Subject);
        Assert.NotNull(first.NextCursor);
        Assert.Equal("initial", Assert.Single(second.Commits).Subject);
        Assert.Null(second.NextCursor);
        DeveloperGitCommitDetailResult rootDetail = await sut.InspectCommitAsync(
            root, second.Commits[0].Sha);
        Assert.Null(Assert.Single(rootDetail.Detail!.ParentDiffs).Parent);
    }

    [Fact]
    public async Task Large_history_is_bounded_paged_and_cancellable()
    {
        await InitializeAsync();
        using (Repository repository = new(root))
        {
            Signature signature = new("History Author", "history@harness.local", DateTimeOffset.UtcNow);
            for (int index = 0; index < 225; index++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), $"revision {index}\n");
                Commands.Stage(repository, "first.txt");
                repository.Commit($"revision {index}", signature, signature);
            }
        }
        var sut = new LibGitDeveloperGitRepository();

        DeveloperGitHistoryPage first = await sut.InspectHistoryAsync(new(
            root, null, null, MaximumResults: 200));
        DeveloperGitHistoryPage second = await sut.InspectHistoryAsync(new(
            root, null, first.NextCursor, MaximumResults: 200));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Equal(200, first.Commits.Count);
        Assert.Equal(26, second.Commits.Count);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sut.InspectHistoryAsync(new(root, null, null, 200), cancelled.Token));
    }

    [Fact]
    public async Task Shows_commit_parent_diff_and_bounded_blame_lines()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "first\nsecond\n");
        string commitSha;
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "first.txt");
            Signature signature = new("Blame Author", "blame@harness.local", DateTimeOffset.UtcNow);
            commitSha = repository.Commit("two lines", signature, signature).Sha;
        }
        var sut = new LibGitDeveloperGitRepository();

        DeveloperGitCommitDetailResult detail = await sut.InspectCommitAsync(
            root, new(commitSha));
        DeveloperGitBlamePage blame = await sut.InspectBlameAsync(new(
            root, new("first.txt"), StartLine: 2, MaximumLines: 1));

        DeveloperGitCommitDetail commit = Assert.IsType<DeveloperGitCommitDetail>(detail.Detail);
        DeveloperGitCommitParentDiff diff = Assert.Single(commit.ParentDiffs);
        Assert.Contains(diff.Paths, path => path.Value == "first.txt");
        Assert.Contains("+second", diff.Patch, StringComparison.Ordinal);
        DeveloperGitBlameLine line = Assert.Single(blame.Lines);
        Assert.Equal(2, line.LineNumber);
        Assert.Equal("second", line.Text);
        Assert.Equal(commitSha, line.Commit.Value);
        Assert.Null(blame.NextStartLine);
    }

    [Fact]
    public async Task Fetch_then_fast_forward_integration_and_push_use_exact_remote_observations()
    {
        await InitializeAsync();
        string remoteRoot = NewWorktreePath();
        Repository.Init(remoteRoot, isBare: true);
        string peerRoot = NewWorktreePath();
        string branchName;
        using (Repository repository = new(root))
        {
            branchName = repository.Head.FriendlyName;
            Remote remote = repository.Network.Remotes.Add("origin", remoteRoot);
            repository.Network.Push(remote,
                $"refs/heads/{branchName}:refs/heads/{branchName}", new PushOptions());
            Commands.Fetch(repository, "origin", [], new FetchOptions(), null);
            repository.Branches.Update(repository.Head, updater =>
            {
                updater.Remote = "origin";
                updater.UpstreamBranch = $"refs/heads/{branchName}";
            });
        }
        Repository.Clone(remoteRoot, peerRoot);
        using (Repository peer = new(peerRoot))
        {
            peer.Config.Set("user.name", "Remote Developer");
            peer.Config.Set("user.email", "remote@harness.local");
            await File.WriteAllTextAsync(Path.Combine(peerRoot, "first.txt"), "remote change\n");
            Commands.Stage(peer, "first.txt");
            Signature signature = new("Remote Developer", "remote@harness.local", DateTimeOffset.UtcNow);
            peer.Commit("remote change", signature, signature);
            peer.Network.Push(peer.Network.Remotes["origin"],
                $"refs/heads/{branchName}:refs/heads/{branchName}", new PushOptions());
        }

        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitRemoteInspection before = await sut.InspectRemotesAsync(root);
        DeveloperGitRemoteResult fetched = await sut.ApplyRemoteAsync(new(
            root, new(before.State!.Fingerprint), DeveloperGitRemoteOperation.Fetch,
            new("origin"), new(branchName), new(branchName), before.LocalSha,
            before.RemoteTrackingSha, DeveloperGitPushPolicy.FastForwardOnly));
        DeveloperGitRemoteResult pulled = await sut.ApplyRemoteAsync(new(
            root, new(fetched.Inspection.State!.Fingerprint), DeveloperGitRemoteOperation.PullMerge,
            new("origin"), new(branchName), new(branchName), fetched.Inspection.LocalSha,
            fetched.Inspection.RemoteTrackingSha, DeveloperGitPushPolicy.FastForwardOnly));

        Assert.Null(fetched.Error);
        Assert.Equal(1, fetched.Inspection.Behind);
        Assert.Null(pulled.Error);
        Assert.Equal("remote change\n", await File.ReadAllTextAsync(Path.Combine(root, "first.txt")));
        Assert.Equal(0, pulled.Inspection.Behind);

        await File.WriteAllTextAsync(Path.Combine(root, "second.txt"), "local push\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "second.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("local push", signature, signature);
        }
        DeveloperGitRemoteInspection pushBefore = await sut.InspectRemotesAsync(root);
        DeveloperGitRemoteResult pushed = await sut.ApplyRemoteAsync(new(
            root, new(pushBefore.State!.Fingerprint), DeveloperGitRemoteOperation.Push,
            new("origin"), new(branchName), new(branchName), pushBefore.LocalSha,
            pushBefore.RemoteTrackingSha, DeveloperGitPushPolicy.FastForwardOnly));
        Assert.Null(pushed.Error);
    }

    [Fact]
    public async Task Remote_inspection_sanitizes_http_credentials_and_query()
    {
        await InitializeAsync();
        using (Repository repository = new(root))
            repository.Network.Remotes.Add("origin",
                "https://user:secret@example.test/repository.git?token=hidden#fragment");

        DeveloperGitRemoteInspection result =
            await new LibGitDeveloperGitRepository().InspectRemotesAsync(root);

        string url = Assert.Single(result.Remotes).SanitizedUrl;
        Assert.DoesNotContain("secret", url, StringComparison.Ordinal);
        Assert.DoesNotContain("token", url, StringComparison.Ordinal);
        Assert.Equal("https://example.test/repository.git", url.TrimEnd('/'));
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

}
