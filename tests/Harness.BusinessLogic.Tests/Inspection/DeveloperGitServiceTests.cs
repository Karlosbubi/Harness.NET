using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Tests.Inspection;

public sealed class DeveloperGitServiceTests
{
    [Fact]
    public async Task Resolves_approved_goal_context_and_preserves_exact_baseline()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(),
            repository,
            new GitInspector());

        DeveloperGitIndexCommandResult result = await service.UpdateIndexAsync(new(
            new(new("workspace-id"), new("goal-id")),
            new("expected-fingerprint"),
            DeveloperGitIndexAction.Stage,
            [new("src/App.cs")]));

        Assert.Equal(WorkbenchWorkspaceScope.ApprovedGoalWorktree, result.Context.Scope);
        Assert.Equal("/state/worktrees/goal-id", repository.Request!.RepositoryRoot);
        Assert.Equal("expected-fingerprint", repository.Request.ExpectedFingerprint.Value);
        Assert.Equal(DeveloperGitIndexOperation.Stage, repository.Request.Operation);
        Assert.Equal("src/App.cs", Assert.Single(repository.Request.Paths).Value);
    }

    [Fact]
    public async Task Patch_selection_preserves_opaque_unit_and_expected_fingerprint()
    {
        Repository repository = new();
        DeveloperGitService service = new(new ContextResolver(), repository, new GitInspector());

        await service.ApplyPatchAsync(new(
            new(new("workspace-id"), new("goal-id")),
            new("displayed-fingerprint"),
            new string('a', 64)));

        Assert.Equal("/state/worktrees/goal-id", repository.PatchRequest!.RepositoryRoot);
        Assert.Equal("displayed-fingerprint", repository.PatchRequest.ExpectedFingerprint.Value);
        Assert.Equal(new string('a', 64), repository.PatchRequest.PatchUnitId);
    }

    [Fact]
    public async Task Destructive_preview_and_apply_bind_original_context_state_and_paths()
    {
        Repository repository = new();
        WorkspaceGitState state = new(
            "main", "head",
            [new("src/App.cs", "ModifiedInWorkdir", WorktreeStatus: "ModifiedInWorkdir",
                IsUnstaged: true)],
            "diff", false, null, null, "displayed-fingerprint");
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector(state));

        DeveloperGitDestructivePreviewResult result = await service.PreviewDestructiveAsync(new(
            new(new("workspace-id"), null),
            new("displayed-fingerprint"),
            DeveloperGitDestructiveAction.DiscardTrackedWorktree,
            [new("src/App.cs")]));

        DeveloperGitDestructivePreviewView preview = Assert.IsType<DeveloperGitDestructivePreviewView>(
            result.Preview);
        Assert.Equal(64, preview.Id.Value.Length);
        Assert.False(preview.HasGuaranteedRecovery);
        Assert.Contains("does not guarantee", preview.Recovery, StringComparison.OrdinalIgnoreCase);

        DeveloperGitIndexCommandResult applied = await service.ApplyDestructiveAsync(preview);

        Assert.Null(applied.Error);
        Assert.Equal("/workspace/repository", repository.DestructiveRequest!.RepositoryRoot);
        Assert.Equal("displayed-fingerprint", repository.DestructiveRequest.ExpectedFingerprint.Value);
        Assert.Equal(DeveloperGitDestructiveOperation.DiscardTrackedWorktree,
            repository.DestructiveRequest.Operation);
        Assert.Equal("src/App.cs", Assert.Single(repository.DestructiveRequest.Paths).Value);
    }

    [Fact]
    public async Task Destructive_preview_rejects_approved_goal_worktree_context()
    {
        DeveloperGitService service = new(
            new ContextResolver(), new Repository(), new GitInspector());

        DeveloperGitDestructivePreviewResult result = await service.PreviewDestructiveAsync(new(
            new(new("workspace-id"), new("goal-id")),
            new("fingerprint"),
            DeveloperGitDestructiveAction.DeleteUntracked,
            [new("scratch.tmp")]));

        Assert.Equal("git_destructive_goal_context_denied", result.ErrorCode);
        Assert.Null(result.Preview);
    }

    [Fact]
    public async Task Commit_preview_and_apply_bind_staged_diff_identity_hooks_and_baseline()
    {
        Repository repository = new();
        WorkspaceGitState state = new(
            "main", new string('a', 40),
            [new("src/App.cs", "ModifiedInIndex", IndexStatus: "ModifiedInIndex", IsStaged: true)],
            "combined", false, null, null, "commit-fingerprint",
            StagedDiff: "exact staged diff");
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector(state));

        DeveloperGitCommitPreviewResult result = await service.PreviewCommitAsync(new(
            new(new("workspace-id"), null), new("commit-fingerprint"),
            DeveloperGitCommitAction.Amend, DeveloperGitCommitHookPolicy.BypassHooks,
            new("Amend message")));

        DeveloperGitCommitPreviewView preview = Assert.IsType<DeveloperGitCommitPreviewView>(result.Preview);
        Assert.Equal("exact staged diff", preview.StagedDiff);
        Assert.Equal("Harness Developer", preview.AuthorName);
        Assert.Equal(DeveloperGitCommitHookPolicy.BypassHooks, preview.HookPolicy);

        DeveloperGitCommitCommandResult applied = await service.CommitAsync(preview);

        Assert.Null(applied.Error);
        Assert.Equal("commit-fingerprint", repository.CommitRequest!.ExpectedFingerprint.Value);
        Assert.Equal(DeveloperGitCommitOperation.Amend, repository.CommitRequest.Operation);
        Assert.Equal(DeveloperGitHookPolicy.BypassHooks, repository.CommitRequest.HookPolicy);
        Assert.Equal("Amend message", repository.CommitRequest.Message);
    }

    [Fact]
    public async Task Developer_commit_rejects_goal_context_without_using_goal_approval()
    {
        DeveloperGitService service = new(
            new ContextResolver(), new Repository(), new GitInspector());

        DeveloperGitCommitPreviewResult result = await service.PreviewCommitAsync(new(
            new(new("workspace-id"), new("goal-id")), new("fingerprint"),
            DeveloperGitCommitAction.Create, DeveloperGitCommitHookPolicy.RunConfiguredHooks,
            new("Message")));

        Assert.Equal("git_commit_goal_context_denied", result.ErrorCode);
        Assert.Null(result.Preview);
    }

    [Fact]
    public async Task Branch_management_binds_original_context_and_exact_reference_fingerprint()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector());

        DeveloperGitBranchInspectionResult inspected = await service.InspectBranchesAsync(
            new(new("workspace-id"), null));
        Assert.Equal(2, inspected.Branches.Count);

        await service.ApplyBranchAsync(new(
            new(new("workspace-id"), null), new("branch-fingerprint"),
            DeveloperGitBranchAction.Rename, new("feature"), new("renamed")));

        Assert.Equal(DeveloperGitBranchOperation.Rename, repository.BranchRequest!.Operation);
        Assert.Equal("branch-fingerprint", repository.BranchRequest.ExpectedFingerprint.Value);
        Assert.Equal("feature", repository.BranchRequest.ExistingName);
        Assert.Equal("renamed", repository.BranchRequest.NewName);
    }

    [Fact]
    public async Task Unmerged_branch_delete_requires_force_and_exact_preview()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector());

        DeveloperGitBranchDeletePreviewResult denied = await service.PreviewBranchDeleteAsync(new(
            new(new("workspace-id"), null), new("branch-fingerprint"), new("feature"), false));
        Assert.Equal("git_branch_unmerged", denied.ErrorCode);

        DeveloperGitBranchDeletePreviewResult result = await service.PreviewBranchDeleteAsync(new(
            new(new("workspace-id"), null), new("branch-fingerprint"), new("feature"), true));
        DeveloperGitBranchDeletePreviewView preview = Assert.IsType<DeveloperGitBranchDeletePreviewView>(
            result.Preview);
        Assert.Contains(new string('b', 40), preview.Consequence, StringComparison.Ordinal);
        Assert.False(preview.HasGuaranteedRecovery);

        await service.ApplyBranchDeleteAsync(preview);

        Assert.Equal(DeveloperGitBranchOperation.Delete, repository.BranchRequest!.Operation);
        Assert.True(repository.BranchRequest.Force);
        Assert.Equal("feature", repository.BranchRequest.ExistingName);
    }

    [Fact]
    public async Task Tag_create_preserves_exact_reference_state_annotation_and_message()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector());

        await service.CreateTagAsync(new(
            new(new("workspace-id"), null), new("tag-fingerprint"), new("v1.0"),
            true, new("Release notes")));

        Assert.Equal(DeveloperGitTagOperation.Create, repository.TagRequest!.Operation);
        Assert.Equal("tag-fingerprint", repository.TagRequest.ExpectedFingerprint.Value);
        Assert.Equal("v1.0", repository.TagRequest.Name);
        Assert.True(repository.TagRequest.Annotated);
        Assert.Equal("Release notes", repository.TagRequest.Message);
    }

    [Fact]
    public async Task Tag_delete_revalidates_exact_target_preview_before_apply()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector());

        DeveloperGitTagDeletePreviewResult result = await service.PreviewTagDeleteAsync(new(
            new(new("workspace-id"), null), new("tag-fingerprint"), new("v1.0")));
        DeveloperGitTagDeletePreviewView preview = Assert.IsType<DeveloperGitTagDeletePreviewView>(
            result.Preview);
        Assert.Contains(new string('a', 40), preview.Consequence, StringComparison.Ordinal);
        Assert.False(preview.HasGuaranteedRecovery);

        await service.ApplyTagDeleteAsync(preview);

        Assert.Equal(DeveloperGitTagOperation.Delete, repository.TagRequest!.Operation);
        Assert.Equal("v1.0", repository.TagRequest.Name);
        Assert.Equal("tag-fingerprint", repository.TagRequest.ExpectedFingerprint.Value);
    }

    [Fact]
    public async Task Worktree_create_preserves_exact_repository_set_path_and_branch_choice()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector());

        await service.CreateWorktreeAsync(new(
            new(new("workspace-id"), null), new("worktree-state"), new("worktree-set"),
            new("/workspace/new-feature"), null, new("new-feature")));

        Assert.Equal(DeveloperGitWorktreeOperation.Create, repository.WorktreeRequest!.Operation);
        Assert.Equal("worktree-state", repository.WorktreeRequest.ExpectedFingerprint.Value);
        Assert.Equal("worktree-set", repository.WorktreeRequest.ExpectedWorktreeFingerprint.Value);
        Assert.Equal("/workspace/new-feature", repository.WorktreeRequest.Path);
        Assert.Equal("new-feature", repository.WorktreeRequest.NewBranch);
        Assert.Null(repository.WorktreeRequest.ExistingBranch);
    }

    [Fact]
    public async Task Dirty_worktree_remove_requires_force_and_revalidates_exact_preview()
    {
        Repository repository = new() { WorktreeIsDirty = true };
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector());

        DeveloperGitWorktreeRemovePreviewResult denied = await service.PreviewWorktreeRemoveAsync(new(
            new(new("workspace-id"), null), new("worktree-state"), new("worktree-set"),
            new("/workspace/feature"), false));
        Assert.Equal("git_worktree_dirty", denied.ErrorCode);

        DeveloperGitWorktreeRemovePreviewResult result = await service.PreviewWorktreeRemoveAsync(new(
            new(new("workspace-id"), null), new("worktree-state"), new("worktree-set"),
            new("/workspace/feature"), true));
        DeveloperGitWorktreeRemovePreviewView preview = Assert.IsType<DeveloperGitWorktreeRemovePreviewView>(
            result.Preview);
        Assert.Contains("uncommitted", preview.Consequence, StringComparison.OrdinalIgnoreCase);
        Assert.False(preview.HasGuaranteedRecovery);

        await service.ApplyWorktreeRemoveAsync(preview);

        Assert.Equal(DeveloperGitWorktreeOperation.Remove, repository.WorktreeRequest!.Operation);
        Assert.Equal("feature-state", repository.WorktreeRequest.ExpectedSelectedWorktreeFingerprint!.Value);
        Assert.True(repository.WorktreeRequest.Force);
    }

    [Fact]
    public async Task Registered_or_harness_managed_worktree_cannot_be_removed()
    {
        Repository registeredRepository = new();
        DeveloperGitService registeredService = new(
            new ContextResolver(goalContext: false), registeredRepository, new GitInspector(),
            new WorkspaceService("/workspace/feature"));
        DeveloperGitWorktreeRemovePreviewResult registered = await registeredService.PreviewWorktreeRemoveAsync(
            new(new(new("workspace-id"), null), new("worktree-state"), new("worktree-set"),
                new("/workspace/feature"), false));
        Assert.Equal("git_worktree_remove_invalid", registered.ErrorCode);
        Assert.Contains("registered", registered.Error!, StringComparison.OrdinalIgnoreCase);

        Repository managedRepository = new() { WorktreeIsHarnessManaged = true };
        DeveloperGitService managedService = new(
            new ContextResolver(goalContext: false), managedRepository, new GitInspector());
        DeveloperGitWorktreeRemovePreviewResult managed = await managedService.PreviewWorktreeRemoveAsync(
            new(new(new("workspace-id"), null), new("worktree-state"), new("worktree-set"),
                new("/workspace/feature"), false));
        Assert.Equal("git_worktree_remove_invalid", managed.ErrorCode);
        Assert.Contains("Harness-managed", managed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stash_create_and_apply_route_exact_original_state_and_commit()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector());

        await service.CreateStashAsync(new(
            new(new("workspace-id"), null), new("stash-state"),
            new("checkpoint"), IncludeUntracked: true));

        Assert.Equal(DeveloperGitStashOperation.Create, repository.StashRequest!.Operation);
        Assert.Equal("checkpoint", repository.StashRequest.Message);
        Assert.True(repository.StashRequest.IncludeUntracked);

        var stash = new DeveloperGitStashCommitSha(new string('c', 40));
        await service.ApplyStashAsync(new(
            new(new("workspace-id"), null), new("stash-state"), stash));

        Assert.Equal(DeveloperGitStashOperation.Apply, repository.StashRequest!.Operation);
        Assert.Equal(stash.Value, repository.StashRequest.ExpectedStashCommitSha);
        Assert.False(repository.StashRequest.IncludeUntracked);
    }

    [Fact]
    public async Task Stash_drop_requires_exact_preview_and_revalidates_before_apply()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(goalContext: false), repository, new GitInspector());
        var stash = new DeveloperGitStashCommitSha(new string('c', 40));

        DeveloperGitStashDropPreviewResult result = await service.PreviewStashDropAsync(new(
            new(new("workspace-id"), null), new("stash-state"), stash));
        DeveloperGitStashDropPreviewView preview = Assert.IsType<DeveloperGitStashDropPreviewView>(
            result.Preview);

        Assert.Equal(stash, preview.Stash.CommitSha);
        Assert.Contains(stash.Value, preview.Consequence, StringComparison.Ordinal);
        Assert.False(preview.HasGuaranteedRecovery);

        await service.ApplyStashDropAsync(preview);

        Assert.Equal(DeveloperGitStashOperation.Drop, repository.StashRequest!.Operation);
        Assert.Equal(stash.Value, repository.StashRequest.ExpectedStashCommitSha);
        Assert.Equal("stash-state", repository.StashRequest.ExpectedFingerprint.Value);
    }

    [Fact]
    public async Task Goal_context_cannot_manage_developer_stashes()
    {
        DeveloperGitService service = new(
            new ContextResolver(goalContext: true), new Repository(), new GitInspector());

        DeveloperGitStashInspectionResult result = await service.InspectStashesAsync(
            new(new("workspace-id"), new("goal-id")));

        Assert.Equal("git_stashes_goal_context_denied", result.ErrorCode);
    }

    private sealed class Repository : IDeveloperGitRepository
    {
        internal bool WorktreeIsDirty { get; init; }
        internal bool WorktreeIsHarnessManaged { get; init; }
        internal DeveloperGitIndexRequest? Request { get; private set; }
        internal DeveloperGitPatchRequest? PatchRequest { get; private set; }
        internal DeveloperGitDestructiveRequest? DestructiveRequest { get; private set; }
        internal DeveloperGitCommitRequest? CommitRequest { get; private set; }
        internal DeveloperGitBranchRequest? BranchRequest { get; private set; }
        internal DeveloperGitTagRequest? TagRequest { get; private set; }
        internal DeveloperGitWorktreeRequest? WorktreeRequest { get; private set; }
        internal DeveloperGitStashRequest? StashRequest { get; private set; }

        public ValueTask<DeveloperGitIndexResult> UpdateIndexAsync(
            DeveloperGitIndexRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(new DeveloperGitIndexResult(null, [], null, null));
        }

        public ValueTask<DeveloperGitIndexResult> ApplyPatchAsync(
            DeveloperGitPatchRequest request,
            CancellationToken cancellationToken = default)
        {
            PatchRequest = request;
            return ValueTask.FromResult(new DeveloperGitIndexResult(null, [], null, null));
        }

        public ValueTask<DeveloperGitIndexResult> ApplyDestructiveAsync(
            DeveloperGitDestructiveRequest request,
            CancellationToken cancellationToken = default)
        {
            DestructiveRequest = request;
            return ValueTask.FromResult(new DeveloperGitIndexResult(null, [], null, null));
        }

        public ValueTask<DeveloperGitCommitIdentityResult> GetCommitIdentityAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperGitCommitIdentityResult(
                new("Harness Developer", "developer@harness.local"), null, null));

        public ValueTask<DeveloperGitCommitResult> CommitAsync(
            DeveloperGitCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            CommitRequest = request;
            return ValueTask.FromResult(new DeveloperGitCommitResult(
                null, new string('c', 40), null, null));
        }

        public ValueTask<DeveloperGitBranchInspection> InspectBranchesAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperGitBranchInspection(
                new("main", new string('a', 40), [], "", false, null, null, "branch-fingerprint"),
                [new("main", new string('a', 40), true, true),
                 new("feature", new string('b', 40), false, false)], null, null));

        public ValueTask<DeveloperGitBranchResult> ApplyBranchAsync(
            DeveloperGitBranchRequest request,
            CancellationToken cancellationToken = default)
        {
            BranchRequest = request;
            return ValueTask.FromResult(new DeveloperGitBranchResult(
                new("main", new string('a', 40), [], "", false, null, null, "after"),
                [new("main", new string('a', 40), true, true)], null, null));
        }

        public ValueTask<DeveloperGitTagInspection> InspectTagsAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperGitTagInspection(
                new("main", new string('a', 40), [], "", false, null, null, "tag-fingerprint"),
                [new("v1.0", new string('a', 40), true, "Release", false)], null, null));

        public ValueTask<DeveloperGitTagResult> ApplyTagAsync(
            DeveloperGitTagRequest request,
            CancellationToken cancellationToken = default)
        {
            TagRequest = request;
            return ValueTask.FromResult(new DeveloperGitTagResult(
                new("main", new string('a', 40), [], "", false, null, null, "after-tag"),
                [], null, null));
        }

        public ValueTask<DeveloperGitWorktreeInspection> InspectWorktreesAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperGitWorktreeInspection(
                new("main", new string('a', 40), [], "", false, null, null, "worktree-state"),
                new("worktree-set"),
                [
                    new("/workspace/repository", "main", new string('a', 40), true, false, null,
                        false, false, false, new("main-state")),
                    new("/workspace/feature", "feature", new string('b', 40), false, false, null,
                        WorktreeIsDirty, false, WorktreeIsHarnessManaged, new("feature-state")),
                ], null, null));

        public ValueTask<DeveloperGitWorktreeResult> ApplyWorktreeAsync(
            DeveloperGitWorktreeRequest request,
            CancellationToken cancellationToken = default)
        {
            WorktreeRequest = request;
            return ValueTask.FromResult(new DeveloperGitWorktreeResult(
                new("main", new string('a', 40), [], "", false, null, null, "after-worktree"),
                new("after-worktree-set"),
                [new("/workspace/repository", "main", new string('a', 40), true, false, null,
                    false, false, false, new("main-state"))], null, null));
        }

        public ValueTask<DeveloperGitStashInspection> InspectStashesAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperGitStashInspection(
                new("main", new string('a', 40), [], "", false, null, null, "stash-state"),
                [new("stash@{0}", new string('c', 40), new string('a', 40),
                    DateTimeOffset.UnixEpoch, "On main: checkpoint", false)], null, null));

        public ValueTask<DeveloperGitStashResult> ApplyStashAsync(
            DeveloperGitStashRequest request,
            CancellationToken cancellationToken = default)
        {
            StashRequest = request;
            return ValueTask.FromResult(new DeveloperGitStashResult(
                new("main", new string('a', 40), [], "", false, null, null, "after-stash"),
                [], request.Operation == DeveloperGitStashOperation.Apply
                    ? request.ExpectedStashCommitSha : null, null, null));
        }
    }

    private sealed class GitInspector(WorkspaceGitState? state = null) : IWorkspaceGitInspector
    {
        public ValueTask<WorkspaceGitState> InspectAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(state ?? new WorkspaceGitState(
                "main", "head", [], "", false, null, null, "fingerprint"));
    }

    private sealed class ContextResolver(bool goalContext = true) : IWorkbenchWorkspaceContextResolver
    {
        public ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkbenchWorkspaceResolution(
                goalContext
                    ? new(request.WorkspaceId, request.GoalId, new("harness/goal"),
                        WorkbenchWorkspaceScope.ApprovedGoalWorktree, "Approved goal worktree")
                    : new(request.WorkspaceId, null, new("main"),
                        WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                goalContext ? "/state/worktrees/goal-id" : "/workspace/repository",
                null,
                null));
    }

    private sealed class WorkspaceService(string registeredRoot) : IWorkspaceService
    {
        public ValueTask<IReadOnlyList<WorkspaceView>> ListAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<WorkspaceView>>(
            [new("registered", registeredRoot, "feature", Path.Combine(registeredRoot, "App.csproj"),
                true, false, "feature", false)]);

        public ValueTask<WorkspaceResult> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<WorkspaceResult> RegisterAsync(string path, string entryPoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceResult> SetTrustAsync(string workspaceId, bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceView?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<WorkspaceView> SelectAsync(string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceResult> RefreshAsync(string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
