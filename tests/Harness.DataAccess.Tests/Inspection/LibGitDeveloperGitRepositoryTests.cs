using Harness.DataAccess.Configuration;
using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class LibGitDeveloperGitRepositoryTests : IDisposable
{
    private readonly List<string> linkedWorktreePaths = [];
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

    [Fact]
    public async Task Stages_and_unstages_one_exact_hunk()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "first changed\n");
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit stage = Assert.Single(before.PatchUnits!, unit =>
            unit.Path.Value == "first.txt" && unit.Direction == DeveloperGitPatchDirection.Stage &&
            unit.Kind == DeveloperGitPatchKind.Hunk);

        DeveloperGitIndexResult staged = await sut.ApplyPatchAsync(new(
            root, new(before.Fingerprint), stage.Id));

        Assert.Null(staged.Error);
        Assert.True(staged.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.False(staged.State.Changes.Single(change => change.Path == "first.txt").IsUnstaged);
        DeveloperGitPatchUnit unstage = Assert.Single(staged.State.PatchUnits!, unit =>
            unit.Path.Value == "first.txt" && unit.Direction == DeveloperGitPatchDirection.Unstage &&
            unit.Kind == DeveloperGitPatchKind.Hunk);

        DeveloperGitIndexResult restored = await sut.ApplyPatchAsync(new(
            root, new(staged.State.Fingerprint), unstage.Id));

        Assert.Null(restored.Error);
        WorkspaceGitFileChange change = Assert.Single(restored.State!.Changes,
            candidate => candidate.Path == "first.txt");
        Assert.False(change.IsStaged);
        Assert.True(change.IsUnstaged);
    }

    [Fact]
    public async Task Stages_individual_replacement_lines_without_staging_the_other_line()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "FIRST\n");
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit deletion = Assert.Single(before.PatchUnits!, unit =>
            unit.Path.Value == "first.txt" && unit.Direction == DeveloperGitPatchDirection.Stage &&
            unit.Kind == DeveloperGitPatchKind.Line && unit.Preview.StartsWith("-first", StringComparison.Ordinal));

        DeveloperGitIndexResult result = await sut.ApplyPatchAsync(new(
            root, new(before.Fingerprint), deletion.Id));

        Assert.Null(result.Error);
        Assert.Equal(string.Empty, ReadIndexText("first.txt"));
        Assert.True(result.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.True(result.State.Changes.Single(change => change.Path == "first.txt").IsUnstaged);
        Assert.Contains(result.State.PatchUnits!, unit =>
            unit.Direction == DeveloperGitPatchDirection.Stage && unit.Kind == DeveloperGitPatchKind.Line &&
            unit.Preview.StartsWith("+FIRST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unstages_individual_replacement_lines_without_unstaging_the_other_line()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "FIRST\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "first.txt");
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit addition = Assert.Single(before.PatchUnits!, unit =>
            unit.Path.Value == "first.txt" && unit.Direction == DeveloperGitPatchDirection.Unstage &&
            unit.Kind == DeveloperGitPatchKind.Line && unit.Preview.StartsWith("+FIRST", StringComparison.Ordinal));

        DeveloperGitIndexResult partial = await sut.ApplyPatchAsync(new(
            root, new(before.Fingerprint), addition.Id));

        Assert.Null(partial.Error);
        Assert.Equal(string.Empty, ReadIndexText("first.txt"));
        DeveloperGitPatchUnit deletion = Assert.Single(partial.State!.PatchUnits!, unit =>
            unit.Direction == DeveloperGitPatchDirection.Unstage && unit.Kind == DeveloperGitPatchKind.Line &&
            unit.Preview.StartsWith("-first", StringComparison.Ordinal));
        DeveloperGitIndexResult restored = await sut.ApplyPatchAsync(new(
            root, new(partial.State.Fingerprint), deletion.Id));
        Assert.Null(restored.Error);
        Assert.Equal("first\n", ReadIndexText("first.txt"));
        Assert.False(restored.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
    }

    [Fact]
    public async Task Rejects_stale_patch_unit_before_index_mutation()
    {
        await InitializeAsync();
        string file = Path.Combine(root, "first.txt");
        await File.WriteAllTextAsync(file, "FIRST\n");
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState displayed = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit unit = Assert.Single(displayed.PatchUnits!, candidate =>
            candidate.Direction == DeveloperGitPatchDirection.Stage && candidate.Kind == DeveloperGitPatchKind.Hunk);
        await File.WriteAllTextAsync(file, "changed again\n");

        DeveloperGitIndexResult result = await sut.ApplyPatchAsync(new(
            root, new(displayed.Fingerprint), unit.Id));

        Assert.Equal("git_state_stale", result.ErrorCode);
        Assert.Equal("first\n", ReadIndexText("first.txt"));
    }

    [Fact]
    public async Task Patch_units_preserve_git_quoted_unicode_paths()
    {
        await InitializeAsync();
        const string path = "über.txt";
        await File.WriteAllTextAsync(Path.Combine(root, path), "before\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, path);
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("unicode path", signature, signature);
        }
        await File.WriteAllTextAsync(Path.Combine(root, path), "after\n");
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit hunk = Assert.Single(before.PatchUnits!, unit =>
            unit.Path.Value == path && unit.Direction == DeveloperGitPatchDirection.Stage &&
            unit.Kind == DeveloperGitPatchKind.Hunk);

        DeveloperGitIndexResult result = await new LibGitDeveloperGitRepository().ApplyPatchAsync(new(
            root, new(before.Fingerprint), hunk.Id));

        Assert.Null(result.Error);
        Assert.Equal("after\n", ReadIndexText(path));
    }

    [Fact]
    public async Task Discard_restores_worktree_from_index_without_unstaging()
    {
        await InitializeAsync();
        string file = Path.Combine(root, "first.txt");
        await File.WriteAllTextAsync(file, "staged\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "first.txt");
        await File.WriteAllTextAsync(file, "unstaged\n");
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);

        DeveloperGitIndexResult result = await new LibGitDeveloperGitRepository().ApplyDestructiveAsync(new(
            root,
            new(before.Fingerprint),
            DeveloperGitDestructiveOperation.DiscardTrackedWorktree,
            [new("first.txt")]));

        Assert.Null(result.Error);
        Assert.Equal("staged\n", await File.ReadAllTextAsync(file));
        Assert.Equal("staged\n", ReadIndexText("first.txt"));
        WorkspaceGitFileChange change = Assert.Single(result.State!.Changes,
            candidate => candidate.Path == "first.txt");
        Assert.True(change.IsStaged);
        Assert.False(change.IsUnstaged);
    }

    [Fact]
    public async Task Cleanup_deletes_only_the_exact_selected_untracked_file()
    {
        await InitializeAsync();
        string selected = Path.Combine(root, "selected.tmp");
        string kept = Path.Combine(root, "kept.tmp");
        await File.WriteAllTextAsync(selected, "delete me");
        await File.WriteAllTextAsync(kept, "keep me");
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);

        DeveloperGitIndexResult result = await new LibGitDeveloperGitRepository().ApplyDestructiveAsync(new(
            root,
            new(before.Fingerprint),
            DeveloperGitDestructiveOperation.DeleteUntracked,
            [new("selected.tmp")]));

        Assert.Null(result.Error);
        Assert.False(File.Exists(selected));
        Assert.True(File.Exists(kept));
        Assert.DoesNotContain(result.State!.Changes, change => change.Path == "selected.tmp");
        Assert.Contains(result.State.Changes, change => change.Path == "kept.tmp");
    }

    [Fact]
    public async Task Cleanup_deletes_an_untracked_symbolic_link_without_deleting_its_target()
    {
        await InitializeAsync();
        string target = Path.Combine(root, "tracked-target.txt");
        await File.WriteAllTextAsync(target, "keep me");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, "tracked-target.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("track target", signature, signature);
        }

        string link = Path.Combine(root, "selected-link.tmp");
        File.CreateSymbolicLink(link, target);
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);

        DeveloperGitIndexResult result = await new LibGitDeveloperGitRepository().ApplyDestructiveAsync(new(
            root,
            new(before.Fingerprint),
            DeveloperGitDestructiveOperation.DeleteUntracked,
            [new("selected-link.tmp")]));

        Assert.Null(result.Error);
        Assert.Null(new FileInfo(link).LinkTarget);
        Assert.True(File.Exists(target));
        Assert.Equal("keep me", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Cleanup_rejects_stale_state_before_deleting_untracked_file()
    {
        await InitializeAsync();
        string selected = Path.Combine(root, "selected.tmp");
        await File.WriteAllTextAsync(selected, "displayed");
        WorkspaceGitState displayed = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        await File.WriteAllTextAsync(selected, "changed later");

        DeveloperGitIndexResult result = await new LibGitDeveloperGitRepository().ApplyDestructiveAsync(new(
            root,
            new(displayed.Fingerprint),
            DeveloperGitDestructiveOperation.DeleteUntracked,
            [new("selected.tmp")]));

        Assert.Equal("git_state_stale", result.ErrorCode);
        Assert.True(File.Exists(selected));
        Assert.Equal("changed later", await File.ReadAllTextAsync(selected));
    }

    [Fact]
    public async Task Commit_creates_exact_staged_commit_and_leaves_unstaged_content()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "staged\n");
        await File.WriteAllTextAsync(Path.Combine(root, "second.txt"), "unstaged\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "first.txt");
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);

        DeveloperGitCommitResult result = await new LibGitDeveloperGitRepository().CommitAsync(new(
            root, new(before.Fingerprint), DeveloperGitCommitOperation.Create,
            DeveloperGitHookPolicy.RunConfiguredHooks, "Developer commit"));

        Assert.Null(result.Error);
        Assert.NotNull(result.CommitSha);
        using Repository after = new(root);
        Assert.Equal("Developer commit", after.Head.Tip!.MessageShort);
        Assert.Equal("staged\n", after.Head.Tip.Tree["first.txt"].Target is Blob blob
            ? blob.GetContentText() : null);
        Assert.Contains(result.State!.Changes, change =>
            change.Path == "second.txt" && change.IsUnstaged && !change.IsStaged);
    }

    [Fact]
    public async Task Initial_commit_has_exact_staged_preview_and_creates_head()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "initial\n");
        using (Repository repository = new(root))
        {
            repository.Config.Set("user.name", "Harness Tests");
            repository.Config.Set("user.email", "tests@harness.local");
            Commands.Stage(repository, "first.txt");
        }
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        Assert.Null(before.HeadSha);
        Assert.Contains("first.txt", before.StagedDiff, StringComparison.Ordinal);
        Assert.Contains("+initial", before.StagedDiff, StringComparison.Ordinal);

        DeveloperGitCommitResult result = await new LibGitDeveloperGitRepository().CommitAsync(new(
            root, new(before.Fingerprint), DeveloperGitCommitOperation.Create,
            DeveloperGitHookPolicy.RunConfiguredHooks, "Initial commit"));

        Assert.Null(result.Error);
        Assert.NotNull(result.CommitSha);
        using Repository after = new(root);
        Assert.Equal("Initial commit", after.Head.Tip!.MessageShort);
    }

    [Fact]
    public async Task Commit_preserves_detached_head_state()
    {
        await InitializeAsync();
        using (Repository repository = new(root))
            Commands.Checkout(repository, repository.Head.Tip!);
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "detached\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "first.txt");
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        Assert.Equal("(detached)", before.Branch);

        DeveloperGitCommitResult result = await new LibGitDeveloperGitRepository().CommitAsync(new(
            root, new(before.Fingerprint), DeveloperGitCommitOperation.Create,
            DeveloperGitHookPolicy.BypassHooks, "Detached commit"));

        Assert.Null(result.Error);
        using Repository after = new(root);
        Assert.True(after.Info.IsHeadDetached);
        Assert.Equal("Detached commit", after.Head.Tip!.MessageShort);
    }

    [Fact]
    public async Task Commit_runs_hooks_unless_bypass_is_explicit()
    {
        if (OperatingSystem.IsWindows()) return;
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "changed\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "first.txt");
        string hook = Path.Combine(root, ".git", "hooks", "pre-commit");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\nexit 1\n");
        File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                  UnixFileMode.UserExecute);
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);

        DeveloperGitCommitResult rejected = await sut.CommitAsync(new(
            root, new(before.Fingerprint), DeveloperGitCommitOperation.Create,
            DeveloperGitHookPolicy.RunConfiguredHooks, "Rejected"));

        Assert.Equal("git_commit_rejected", rejected.ErrorCode);
        DeveloperGitCommitResult bypassed = await sut.CommitAsync(new(
            root, new(rejected.State!.Fingerprint), DeveloperGitCommitOperation.Create,
            DeveloperGitHookPolicy.BypassHooks, "Bypassed"));
        Assert.Null(bypassed.Error);
        using Repository after = new(root);
        Assert.Equal("Bypassed", after.Head.Tip!.MessageShort);
    }

    [Fact]
    public async Task Amend_replaces_head_and_keeps_its_parent()
    {
        await InitializeAsync();
        string originalHead;
        string[] originalParents;
        using (var beforeRepository = new Repository(root))
        {
            originalHead = beforeRepository.Head.Tip!.Sha;
            originalParents = beforeRepository.Head.Tip.Parents.Select(parent => parent.Sha).ToArray();
        }
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "amended\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "first.txt");
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);

        DeveloperGitCommitResult result = await new LibGitDeveloperGitRepository().CommitAsync(new(
            root, new(before.Fingerprint), DeveloperGitCommitOperation.Amend,
            DeveloperGitHookPolicy.BypassHooks, "Amended commit"));

        Assert.Null(result.Error);
        Assert.NotEqual(originalHead, result.CommitSha);
        using Repository after = new(root);
        Assert.Equal(originalParents, after.Head.Tip!.Parents.Select(parent => parent.Sha).ToArray());
        Assert.Equal("Amended commit", after.Head.Tip.MessageShort);
    }

    [Fact]
    public async Task Branch_create_rename_and_switch_use_exact_reference_state()
    {
        await InitializeAsync();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitBranchInspection first = await sut.InspectBranchesAsync(root);

        DeveloperGitBranchResult created = await sut.ApplyBranchAsync(new(
            root, new(first.State!.Fingerprint), DeveloperGitBranchOperation.Create,
            null, "feature/local", false));
        Assert.Null(created.Error);
        Assert.Contains(created.Branches, branch => branch.Name == "feature/local" && !branch.IsCurrent);

        DeveloperGitBranchResult renamed = await sut.ApplyBranchAsync(new(
            root, new(created.State!.Fingerprint), DeveloperGitBranchOperation.Rename,
            "feature/local", "feature/renamed", false));
        Assert.Null(renamed.Error);
        DeveloperGitBranchResult switched = await sut.ApplyBranchAsync(new(
            root, new(renamed.State!.Fingerprint), DeveloperGitBranchOperation.Switch,
            "feature/renamed", null, false));
        Assert.Null(switched.Error);
        Assert.Equal("feature/renamed", switched.State!.Branch);
        Assert.True(switched.Branches.Single(branch => branch.Name == "feature/renamed").IsCurrent);
    }

    [Fact]
    public async Task Branch_operation_rejects_reference_change_after_display()
    {
        await InitializeAsync();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitBranchInspection displayed = await sut.InspectBranchesAsync(root);
        using (Repository repository = new(root)) repository.CreateBranch("external");

        DeveloperGitBranchResult result = await sut.ApplyBranchAsync(new(
            root, new(displayed.State!.Fingerprint), DeveloperGitBranchOperation.Create,
            null, "requested", false));

        Assert.Equal("git_state_stale", result.ErrorCode);
        Assert.DoesNotContain(result.Branches, branch => branch.Name == "requested");
    }

    [Fact]
    public async Task Unmerged_branch_requires_explicit_force_deletion()
    {
        await InitializeAsync();
        string baseBranch;
        using (Repository repository = new(root))
        {
            baseBranch = repository.Head.FriendlyName;
            Branch feature = repository.CreateBranch("feature");
            Commands.Checkout(repository, feature);
            File.WriteAllText(Path.Combine(root, "first.txt"), "feature\n");
            Commands.Stage(repository, "first.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("feature commit", signature, signature);
            Commands.Checkout(repository, repository.Branches[baseBranch]!);
        }
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitBranchInspection before = await sut.InspectBranchesAsync(root);
        Assert.False(before.Branches.Single(branch => branch.Name == "feature").IsMergedIntoHead);

        DeveloperGitBranchResult rejected = await sut.ApplyBranchAsync(new(
            root, new(before.State!.Fingerprint), DeveloperGitBranchOperation.Delete,
            "feature", null, false));
        Assert.Equal("git_branch_invalid", rejected.ErrorCode);

        DeveloperGitBranchResult forced = await sut.ApplyBranchAsync(new(
            root, new(rejected.State!.Fingerprint), DeveloperGitBranchOperation.Delete,
            "feature", null, true));
        Assert.Null(forced.Error);
        Assert.DoesNotContain(forced.Branches, branch => branch.Name == "feature");
    }

    [Fact]
    public async Task Branch_switch_reports_dirty_checkout_conflict_without_losing_content()
    {
        await InitializeAsync();
        string baseBranch;
        using (Repository repository = new(root))
        {
            baseBranch = repository.Head.FriendlyName;
            Branch feature = repository.CreateBranch("feature");
            Commands.Checkout(repository, feature);
            File.WriteAllText(Path.Combine(root, "first.txt"), "feature\n");
            Commands.Stage(repository, "first.txt");
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("feature", signature, signature);
            Commands.Checkout(repository, repository.Branches[baseBranch]!);
        }
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "local dirty\n");
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitBranchInspection before = await sut.InspectBranchesAsync(root);

        DeveloperGitBranchResult result = await sut.ApplyBranchAsync(new(
            root, new(before.State!.Fingerprint), DeveloperGitBranchOperation.Switch,
            "feature", null, false));

        Assert.Equal("git_branch_checkout_conflict", result.ErrorCode);
        Assert.Equal("local dirty\n", await File.ReadAllTextAsync(Path.Combine(root, "first.txt")));
        using Repository after = new(root);
        Assert.Equal(baseBranch, after.Head.FriendlyName);
    }

    [Fact]
    public async Task Tags_create_lightweight_and_annotated_then_delete_exact_reference()
    {
        await InitializeAsync();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitTagInspection initial = await sut.InspectTagsAsync(root);
        Assert.Empty(initial.Tags);

        DeveloperGitTagResult lightweight = await sut.ApplyTagAsync(new(
            root, new(initial.State!.Fingerprint), DeveloperGitTagOperation.Create,
            "v1.0", false, null));
        Assert.Null(lightweight.Error);
        DeveloperGitTag light = Assert.Single(lightweight.Tags);
        Assert.False(light.IsAnnotated);

        DeveloperGitTagResult annotated = await sut.ApplyTagAsync(new(
            root, new(lightweight.State!.Fingerprint), DeveloperGitTagOperation.Create,
            "v1.1", true, "Release notes"));
        Assert.Null(annotated.Error);
        DeveloperGitTag annotation = annotated.Tags.Single(tag => tag.Name == "v1.1");
        Assert.True(annotation.IsAnnotated);
        Assert.Equal("Release notes", annotation.Message);
        Assert.Equal(light.TargetSha, annotation.TargetSha);

        DeveloperGitTagResult deleted = await sut.ApplyTagAsync(new(
            root, new(annotated.State!.Fingerprint), DeveloperGitTagOperation.Delete,
            "v1.0", false, null));
        Assert.Null(deleted.Error);
        Assert.DoesNotContain(deleted.Tags, tag => tag.Name == "v1.0");
        Assert.Contains(deleted.Tags, tag => tag.Name == "v1.1");
    }

    [Fact]
    public async Task Tag_operation_rejects_reference_change_after_display()
    {
        await InitializeAsync();
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitTagInspection displayed = await sut.InspectTagsAsync(root);
        using (Repository repository = new(root)) repository.ApplyTag("external", repository.Head.Tip!.Sha);

        DeveloperGitTagResult result = await sut.ApplyTagAsync(new(
            root, new(displayed.State!.Fingerprint), DeveloperGitTagOperation.Create,
            "requested", false, null));

        Assert.Equal("git_state_stale", result.ErrorCode);
        Assert.DoesNotContain(result.Tags, tag => tag.Name == "requested");
        Assert.Contains(result.Tags, tag => tag.Name == "external");
    }

    [Fact]
    public async Task Annotated_tag_requires_configured_identity_and_message()
    {
        await InitializeAsync();
        using (Repository repository = new(root))
        {
            repository.Config.Set("user.name", string.Empty);
            repository.Config.Set("user.email", string.Empty);
        }
        var sut = new LibGitDeveloperGitRepository();
        DeveloperGitTagInspection before = await sut.InspectTagsAsync(root);
        DeveloperGitTagResult noMessage = await sut.ApplyTagAsync(new(
            root, new(before.State!.Fingerprint), DeveloperGitTagOperation.Create,
            "v1", true, null));
        Assert.Equal("git_tag_message_invalid", noMessage.ErrorCode);

        DeveloperGitTagResult noIdentity = await sut.ApplyTagAsync(new(
            root, new(noMessage.State!.Fingerprint), DeveloperGitTagOperation.Create,
            "v1", true, "Release"));
        Assert.Equal("git_identity_missing", noIdentity.ErrorCode);
        Assert.Empty(noIdentity.Tags);
    }

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
        repository.Config.Set("user.name", "Harness Tests");
        repository.Config.Set("user.email", "tests@harness.local");
        Commands.Stage(repository, "*");
        Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
        repository.Commit("initial", signature, signature);
    }

    private string NewWorktreePath()
    {
        string path = root + "-linked-" + Guid.NewGuid().ToString("N");
        linkedWorktreePaths.Add(path);
        return path;
    }

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }

    private string ReadIndexText(string path)
    {
        using Repository repository = new(root);
        IndexEntry entry = repository.Index[path];
        return repository.Lookup<Blob>(entry.Id).GetContentText();
    }

    public void Dispose()
    {
        foreach (string path in linkedWorktreePaths)
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
