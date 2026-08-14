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

    private sealed class Repository : IDeveloperGitRepository
    {
        internal DeveloperGitIndexRequest? Request { get; private set; }
        internal DeveloperGitPatchRequest? PatchRequest { get; private set; }
        internal DeveloperGitDestructiveRequest? DestructiveRequest { get; private set; }

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
}
