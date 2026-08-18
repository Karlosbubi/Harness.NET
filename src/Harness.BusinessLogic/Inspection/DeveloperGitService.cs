using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Inspection;

internal sealed class DeveloperGitService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IDeveloperGitRepository repository,
    IWorkspaceGitInspector gitInspector,
    IWorkspaceService? workspaceService = null) : IDeveloperGitService
{
    public async ValueTask<DeveloperGitIndexCommandResult> UpdateIndexAsync(
        DeveloperGitIndexCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace,
            cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
        {
            return new(resolution.Context, null, [],
                resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The workspace context is unavailable.");
        }

        DeveloperGitIndexResult result = await repository.UpdateIndexAsync(
            new(
                resolution.RootPath,
                new(command.ExpectedFingerprint.Value),
                command.Action == DeveloperGitIndexAction.Stage
                    ? DeveloperGitIndexOperation.Stage
                    : DeveloperGitIndexOperation.Unstage,
                command.Paths.Select(path => new Harness.DataAccess.Inspection.DeveloperGitPath(
                    path.Value)).ToArray()),
            cancellationToken);
        return new(
            resolution.Context,
            result.State is null ? null : Map(result.State),
            result.AffectedPaths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<DeveloperGitIndexCommandResult> ApplyPatchAsync(
        DeveloperGitPatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [],
                resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The workspace context is unavailable.");

        DeveloperGitIndexResult result = await repository.ApplyPatchAsync(new(
            resolution.RootPath,
            new(command.ExpectedFingerprint.Value),
            command.PatchUnitId), cancellationToken);
        return new(
            resolution.Context,
            result.State is null ? null : Map(result.State),
            result.AffectedPaths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<DeveloperGitDestructivePreviewResult> PreviewDestructiveAsync(
        DeveloperGitDestructivePreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(null, null, resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The workspace context is unavailable.");
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(null, null, "git_destructive_goal_context_denied",
                "Destructive Git actions are available only in the original workspace context.");

        WorkspaceGitState state = await gitInspector.InspectAsync(resolution.RootPath, cancellationToken);
        WorkspaceGitStateView view = Map(state);
        if (state.Error is not null) return new(null, view, state.ErrorCode, state.Error);
        if (!state.Fingerprint.Equals(command.ExpectedFingerprint.Value, StringComparison.Ordinal))
            return new(null, view, "git_state_stale",
                "Git state changed after it was displayed. The view was refreshed; review it and retry.");
        if (!TryValidateDestructive(command.Action, command.Paths, view, out string? error))
            return new(null, view, "git_destructive_invalid", error);

        DeveloperGitPath[] paths = command.Paths
            .DistinctBy(path => path.Value, StringComparer.Ordinal)
            .OrderBy(path => path.Value, StringComparer.Ordinal)
            .ToArray();
        string title = command.Action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
            ? $"Discard working-tree changes in {paths.Length} tracked path(s)?"
            : $"Permanently delete {paths.Length} untracked path(s)?";
        string consequence = command.Action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
            ? "The selected working-tree files will be replaced by their current index versions. Staged content stays staged."
            : "The selected untracked files or symbolic links will be deleted from disk. Tracked and staged content is not changed.";
        const string recovery = "Git does not guarantee recovery for this content. Copy anything you need before continuing.";
        var fingerprint = new DeveloperGitStateFingerprint(state.Fingerprint);
        var preview = new DeveloperGitDestructivePreviewView(
            new(PreviewId(resolution.Context, fingerprint, command.Action, paths)),
            resolution.Context,
            fingerprint,
            command.Action,
            paths,
            title,
            consequence,
            recovery,
            HasGuaranteedRecovery: false);
        return new(preview, view, null, null);
    }

    public async ValueTask<DeveloperGitIndexCommandResult> ApplyDestructiveAsync(
        DeveloperGitDestructivePreviewView preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DeveloperGitDestructivePreviewResult current = await PreviewDestructiveAsync(new(
            new(preview.Context.WorkspaceId, GoalId: null),
            preview.Fingerprint,
            preview.Action,
            preview.Paths), cancellationToken);
        if (current.Preview is null || current.Error is not null)
            return new(preview.Context, current.State, [], current.ErrorCode, current.Error);
        if (!current.Preview.Id.Equals(preview.Id))
            return new(preview.Context, current.State, [], "git_destructive_preview_stale",
                "The destructive preview no longer matches. Review a new preview before continuing.");

        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(preview.Context.WorkspaceId, GoalId: null), cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], resolution.ErrorCode, resolution.Error);
        DeveloperGitIndexResult result = await repository.ApplyDestructiveAsync(new(
            resolution.RootPath,
            new(preview.Fingerprint.Value),
            preview.Action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
                ? DeveloperGitDestructiveOperation.DiscardTrackedWorktree
                : DeveloperGitDestructiveOperation.DeleteUntracked,
            preview.Paths.Select(path => new Harness.DataAccess.Inspection.DeveloperGitPath(
                path.Value)).ToArray()), cancellationToken);
        return new(
            resolution.Context,
            result.State is null ? null : Map(result.State),
            result.AffectedPaths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<DeveloperGitCommitPreviewResult> PreviewCommitAsync(
        DeveloperGitCommitPreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Message.Value) || command.Message.Value.Length > 32_768)
            return new(null, null, "git_commit_message_invalid",
                "Enter a commit message between 1 and 32,768 characters.");
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(null, null, resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The workspace context is unavailable.");
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(null, null, "git_commit_goal_context_denied",
                "Developer commits are available only in the original workspace. Goal commits use exact goal approval.");

        WorkspaceGitState state = await gitInspector.InspectAsync(resolution.RootPath, cancellationToken);
        WorkspaceGitStateView view = Map(state);
        if (state.Error is not null) return new(null, view, state.ErrorCode, state.Error);
        if (!state.Fingerprint.Equals(command.ExpectedFingerprint.Value, StringComparison.Ordinal))
            return new(null, view, "git_state_stale",
                "Git state changed after it was displayed. Review the refreshed state and retry.");
        if (state.IsTruncated)
            return new(null, view, "git_commit_preview_truncated",
                "The staged diff is too large for an exact commit preview.");
        if (state.Changes.Any(change => change.IsConflicted))
            return new(null, view, "git_conflicts_present", "Resolve every Git conflict before committing.");
        DeveloperGitPath[] staged = state.Changes.Where(change => change.IsStaged)
            .Select(change => new DeveloperGitPath(change.Path)).OrderBy(path => path.Value, StringComparer.Ordinal)
            .ToArray();
        if (staged.Length == 0)
            return new(null, view, "git_nothing_staged", "Stage at least one change before committing.");
        if (command.Action == DeveloperGitCommitAction.Amend && state.HeadSha is null)
            return new(null, view, "git_amend_unborn", "An unborn branch has no commit to amend.");
        DeveloperGitCommitIdentityResult identity = await repository.GetCommitIdentityAsync(
            resolution.RootPath, cancellationToken);
        if (identity.Identity is null)
            return new(null, view, identity.ErrorCode, identity.Error);

        string previewIdentity = string.Join('\0', resolution.Context.WorkspaceId.Value,
            state.Fingerprint, command.Action, command.HookPolicy, command.Message.Value,
            identity.Identity.Name, identity.Identity.Email);
        var preview = new DeveloperGitCommitPreviewView(
            new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(previewIdentity))).ToLowerInvariant()),
            resolution.Context,
            new(state.Fingerprint),
            command.Action,
            command.HookPolicy,
            command.Message,
            state.Branch,
            state.HeadSha,
            identity.Identity.Name,
            identity.Identity.Email,
            staged,
            state.StagedDiff ?? string.Empty,
            command.Action == DeveloperGitCommitAction.Amend
                ? "The current HEAD commit will be replaced by a new commit containing the staged tree and this message."
                : "A new commit containing the staged tree and this message will be added at HEAD.",
            command.Action == DeveloperGitCommitAction.Amend
                ? "Git normally retains the replaced commit in the local reflog until expiration, but Harness does not guarantee recovery."
                : "The new commit can be reverted or reset using ordinary Git history operations.",
            HasGuaranteedRecovery: false);
        return new(preview, view, null, null);
    }

    public async ValueTask<DeveloperGitCommitCommandResult> CommitAsync(
        DeveloperGitCommitPreviewView preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DeveloperGitCommitPreviewResult current = await PreviewCommitAsync(new(
            new(preview.Context.WorkspaceId, GoalId: null), preview.Fingerprint,
            preview.Action, preview.HookPolicy, preview.Message), cancellationToken);
        if (current.Preview is null || current.Error is not null)
            return new(preview.Context, current.State, null, current.ErrorCode, current.Error);
        if (!current.Preview.Id.Equals(preview.Id))
            return new(preview.Context, current.State, null, "git_commit_preview_stale",
                "The commit preview changed. Review a new preview before committing.");
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(preview.Context.WorkspaceId, GoalId: null), cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null, resolution.ErrorCode, resolution.Error);
        DeveloperGitCommitResult result = await repository.CommitAsync(new(
            resolution.RootPath,
            new(preview.Fingerprint.Value),
            preview.Action == DeveloperGitCommitAction.Create
                ? DeveloperGitCommitOperation.Create : DeveloperGitCommitOperation.Amend,
            preview.HookPolicy == DeveloperGitCommitHookPolicy.RunConfiguredHooks
                ? DeveloperGitHookPolicy.RunConfiguredHooks : DeveloperGitHookPolicy.BypassHooks,
            preview.Message.Value), cancellationToken);
        return new(resolution.Context, result.State is null ? null : Map(result.State),
            result.CommitSha, result.ErrorCode, result.Error);
    }

    public async ValueTask<DeveloperGitBranchInspectionResult> InspectBranchesAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, [], "git_branches_goal_context_denied",
                "Developer branch management is available only in the original workspace.");
        DeveloperGitBranchInspection inspection = await repository.InspectBranchesAsync(
            resolution.RootPath, cancellationToken);
        return MapBranches(resolution.Context, inspection);
    }

    public async ValueTask<DeveloperGitBranchInspectionResult> ApplyBranchAsync(
        DeveloperGitBranchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, [], "git_branches_goal_context_denied",
                "Developer branch management is available only in the original workspace.");
        DeveloperGitBranchResult result = await repository.ApplyBranchAsync(new(
            resolution.RootPath,
            new(command.ExpectedFingerprint.Value),
            command.Action switch
            {
                DeveloperGitBranchAction.Create => DeveloperGitBranchOperation.Create,
                DeveloperGitBranchAction.Switch => DeveloperGitBranchOperation.Switch,
                DeveloperGitBranchAction.Rename => DeveloperGitBranchOperation.Rename,
                _ => throw new InvalidOperationException("Unsupported branch action."),
            },
            command.ExistingName?.Value,
            command.NewName?.Value,
            Force: false), cancellationToken);
        return MapBranches(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitBranchDeletePreviewResult> PreviewBranchDeleteAsync(
        DeveloperGitBranchDeletePreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DeveloperGitBranchInspectionResult inspection = await InspectBranchesAsync(
            command.Workspace, cancellationToken);
        if (inspection.Error is not null || inspection.State is null)
            return new(null, inspection, inspection.ErrorCode, inspection.Error);
        if (!inspection.State.Fingerprint.Equals(command.ExpectedFingerprint.Value, StringComparison.Ordinal))
            return new(null, inspection, "git_state_stale",
                "Git references or working state changed after they were displayed. Refresh and retry.");
        DeveloperGitBranchView? branch = inspection.Branches.SingleOrDefault(candidate =>
            candidate.Name.Value.Equals(command.Name.Value, StringComparison.Ordinal));
        if (branch is null || branch.IsCurrent)
            return new(null, inspection, "git_branch_delete_invalid",
                branch is null ? "Select an existing local branch." : "The current branch cannot be deleted.");
        if (!command.Force && !branch.IsMergedIntoHead)
            return new(null, inspection, "git_branch_unmerged",
                "This branch is not merged into HEAD. Enable force deletion only after reviewing its tip.");
        string identity = string.Join('\0', inspection.Context.WorkspaceId.Value,
            inspection.State.Fingerprint, branch.Name.Value, branch.TipSha, command.Force);
        var preview = new DeveloperGitBranchDeletePreviewView(
            new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()),
            inspection.Context, new(inspection.State.Fingerprint), branch, command.Force,
            command.Force && !branch.IsMergedIntoHead
                ? $"The unmerged local branch '{branch.Name.Value}' will be deleted at {branch.TipSha}."
                : $"The local branch '{branch.Name.Value}' will be deleted at {branch.TipSha}.",
            "The tip commit may remain addressable by this SHA or reflog until Git prunes it, but Harness does not guarantee recovery.",
            HasGuaranteedRecovery: false);
        return new(preview, inspection, null, null);
    }

    public async ValueTask<DeveloperGitBranchInspectionResult> ApplyBranchDeleteAsync(
        DeveloperGitBranchDeletePreviewView preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DeveloperGitBranchDeletePreviewResult current = await PreviewBranchDeleteAsync(new(
            new(preview.Context.WorkspaceId, GoalId: null), preview.Fingerprint,
            preview.Branch.Name, preview.Force), cancellationToken);
        if (current.Preview is null || current.Error is not null)
            return current.Inspection;
        if (!current.Preview.Id.Equals(preview.Id))
            return current.Inspection with
            {
                ErrorCode = "git_branch_delete_preview_stale",
                Error = "The branch deletion preview changed. Review a new preview before deleting.",
            };
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(preview.Context.WorkspaceId, GoalId: null), cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], resolution.ErrorCode, resolution.Error);
        DeveloperGitBranchResult result = await repository.ApplyBranchAsync(new(
            resolution.RootPath, new(preview.Fingerprint.Value), DeveloperGitBranchOperation.Delete,
            preview.Branch.Name.Value, null, preview.Force), cancellationToken);
        return MapBranches(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitTagInspectionResult> InspectTagsAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, [], "git_tags_goal_context_denied",
                "Developer tag management is available only in the original workspace.");
        DeveloperGitTagInspection inspection = await repository.InspectTagsAsync(
            resolution.RootPath, cancellationToken);
        return MapTags(resolution.Context, inspection);
    }

    public async ValueTask<DeveloperGitTagInspectionResult> CreateTagAsync(
        DeveloperGitTagCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, [], "git_tags_goal_context_denied",
                "Developer tag management is available only in the original workspace.");
        DeveloperGitTagResult result = await repository.ApplyTagAsync(new(
            resolution.RootPath, new(command.ExpectedFingerprint.Value), DeveloperGitTagOperation.Create,
            command.Name.Value, command.Annotated, command.Message?.Value), cancellationToken);
        return MapTags(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitTagDeletePreviewResult> PreviewTagDeleteAsync(
        DeveloperGitTagDeletePreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DeveloperGitTagInspectionResult inspection = await InspectTagsAsync(
            command.Workspace, cancellationToken);
        if (inspection.Error is not null || inspection.State is null)
            return new(null, inspection, inspection.ErrorCode, inspection.Error);
        if (!inspection.State.Fingerprint.Equals(command.ExpectedFingerprint.Value, StringComparison.Ordinal))
            return new(null, inspection, "git_state_stale",
                "Git references or working state changed after they were displayed. Refresh and retry.");
        DeveloperGitTagView? tag = inspection.Tags.SingleOrDefault(candidate => candidate.Name == command.Name);
        if (tag is null)
            return new(null, inspection, "git_tag_delete_invalid", "Select an existing local tag.");
        string identity = string.Join('\0', inspection.Context.WorkspaceId.Value,
            inspection.State.Fingerprint, tag.Name.Value, tag.TargetSha);
        var preview = new DeveloperGitTagDeletePreviewView(
            new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()),
            inspection.Context, new(inspection.State.Fingerprint), tag,
            $"The local tag '{tag.Name.Value}' pointing to {tag.TargetSha} will be deleted.",
            "The target object remains while reachable elsewhere, but Harness does not guarantee tag-name or object recovery.",
            HasGuaranteedRecovery: false);
        return new(preview, inspection, null, null);
    }

    public async ValueTask<DeveloperGitTagInspectionResult> ApplyTagDeleteAsync(
        DeveloperGitTagDeletePreviewView preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DeveloperGitTagDeletePreviewResult current = await PreviewTagDeleteAsync(new(
            new(preview.Context.WorkspaceId, GoalId: null), preview.Fingerprint,
            preview.Tag.Name), cancellationToken);
        if (current.Preview is null || current.Error is not null) return current.Inspection;
        if (!current.Preview.Id.Equals(preview.Id))
            return current.Inspection with
            {
                ErrorCode = "git_tag_delete_preview_stale",
                Error = "The tag deletion preview changed. Review a new preview before deleting.",
            };
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(preview.Context.WorkspaceId, GoalId: null), cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], resolution.ErrorCode, resolution.Error);
        DeveloperGitTagResult result = await repository.ApplyTagAsync(new(
            resolution.RootPath, new(preview.Fingerprint.Value), DeveloperGitTagOperation.Delete,
            preview.Tag.Name.Value, false, null), cancellationToken);
        return MapTags(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitWorktreeInspectionResult> InspectWorktreesAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null, [], resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, null, [], "git_worktrees_goal_context_denied",
                "Developer worktree management is available only in the original workspace.");
        DeveloperGitWorktreeInspection inspection = await repository.InspectWorktreesAsync(
            resolution.RootPath, cancellationToken);
        return await MapWorktreesAsync(resolution.Context, inspection, cancellationToken);
    }

    public async ValueTask<DeveloperGitWorktreeInspectionResult> CreateWorktreeAsync(
        DeveloperGitWorktreeCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null, [], resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, null, [], "git_worktrees_goal_context_denied",
                "Developer worktree management is available only in the original workspace.");
        DeveloperGitWorktreeResult result = await repository.ApplyWorktreeAsync(new(
            resolution.RootPath,
            new(command.ExpectedFingerprint.Value),
            new(command.ExpectedWorktreeFingerprint.Value),
            DeveloperGitWorktreeOperation.Create,
            command.Path.Value,
            command.ExistingBranch?.Value,
            command.NewBranch?.Value,
            ExpectedSelectedWorktreeFingerprint: null,
            Force: false), cancellationToken);
        return await MapWorktreesAsync(resolution.Context, result, cancellationToken);
    }

    public async ValueTask<DeveloperGitWorktreeRemovePreviewResult> PreviewWorktreeRemoveAsync(
        DeveloperGitWorktreeRemovePreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DeveloperGitWorktreeInspectionResult inspection = await InspectWorktreesAsync(
            command.Workspace, cancellationToken);
        if (inspection.Error is not null || inspection.State is null ||
            inspection.WorktreeFingerprint is null)
            return new(null, inspection, inspection.ErrorCode, inspection.Error);
        if (!inspection.State.Fingerprint.Equals(command.ExpectedFingerprint.Value, StringComparison.Ordinal) ||
            !inspection.WorktreeFingerprint.Equals(command.ExpectedWorktreeFingerprint))
            return new(null, inspection, "git_state_stale",
                "Git references, working state, or linked worktrees changed after display. Refresh and retry.");
        DeveloperGitWorktreeView? worktree = inspection.Worktrees.SingleOrDefault(candidate =>
            candidate.Path == command.Path);
        if (worktree is null || worktree.IsMain || worktree.IsHarnessManaged ||
            worktree.IsRegisteredWorkspace || worktree.IsLocked)
        {
            string error = worktree switch
            {
                null => "Select an existing linked worktree.",
                { IsMain: true } => "The original workspace cannot be removed as a linked worktree.",
                { IsHarnessManaged: true } => "Harness-managed goal worktrees cannot be removed here.",
                { IsRegisteredWorkspace: true } =>
                    "A registered workspace cannot be removed. Keep it available or remove its registration first.",
                _ => "Unlock this worktree with Git before removing it.",
            };
            return new(null, inspection, "git_worktree_remove_invalid", error);
        }
        if ((worktree.IsDirty || worktree.HasConflicts) && !command.Force)
            return new(null, inspection, "git_worktree_dirty",
                "This worktree has uncommitted content. Review it and explicitly enable forced removal.");

        string identity = string.Join('\0', inspection.Context.WorkspaceId.Value,
            inspection.State.Fingerprint, inspection.WorktreeFingerprint.Value,
            worktree.Path.Value, worktree.StateFingerprint.Value, worktree.Branch?.Value,
            worktree.HeadSha, command.Force);
        bool losesContent = worktree.IsDirty || worktree.HasConflicts;
        var preview = new DeveloperGitWorktreeRemovePreviewView(
            new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()),
            inspection.Context,
            new(inspection.State.Fingerprint),
            inspection.WorktreeFingerprint,
            worktree,
            command.Force,
            losesContent
                ? $"The linked worktree at '{worktree.Path.Value}' and all uncommitted content in it will be deleted."
                : $"The clean linked worktree at '{worktree.Path.Value}' will be deleted. Its branch is kept.",
            losesContent
                ? "Committed objects and the local branch remain, but Git does not recover deleted uncommitted files."
                : "The local branch and committed objects remain and can be checked out into another worktree.",
            HasGuaranteedRecovery: !losesContent);
        return new(preview, inspection, null, null);
    }

    public async ValueTask<DeveloperGitWorktreeInspectionResult> ApplyWorktreeRemoveAsync(
        DeveloperGitWorktreeRemovePreviewView preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DeveloperGitWorktreeRemovePreviewResult current = await PreviewWorktreeRemoveAsync(new(
            new(preview.Context.WorkspaceId, GoalId: null), preview.Fingerprint,
            preview.WorktreeFingerprint, preview.Worktree.Path, preview.Force), cancellationToken);
        if (current.Preview is null || current.Error is not null) return current.Inspection;
        if (!current.Preview.Id.Equals(preview.Id))
            return current.Inspection with
            {
                ErrorCode = "git_worktree_remove_preview_stale",
                Error = "The worktree removal preview changed. Review a new preview before deleting.",
            };
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(preview.Context.WorkspaceId, GoalId: null), cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null, [], resolution.ErrorCode, resolution.Error);
        DeveloperGitWorktreeResult result = await repository.ApplyWorktreeAsync(new(
            resolution.RootPath,
            new(preview.Fingerprint.Value),
            new(preview.WorktreeFingerprint.Value),
            DeveloperGitWorktreeOperation.Remove,
            preview.Worktree.Path.Value,
            ExistingBranch: null,
            NewBranch: null,
            new(preview.Worktree.StateFingerprint.Value),
            preview.Force), cancellationToken);
        return await MapWorktreesAsync(resolution.Context, result, cancellationToken);
    }

    public async ValueTask<DeveloperGitStashInspectionResult> InspectStashesAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], null,
                resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, [], null, "git_stashes_goal_context_denied",
                "Developer stash management is available only in the original workspace.");
        DeveloperGitStashInspection inspection = await repository.InspectStashesAsync(
            resolution.RootPath, cancellationToken);
        return MapStashes(resolution.Context, inspection);
    }

    public async ValueTask<DeveloperGitStashInspectionResult> CreateStashAsync(
        DeveloperGitStashCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], null, resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, [], null, "git_stashes_goal_context_denied",
                "Developer stash management is available only in the original workspace.");
        DeveloperGitStashResult result = await repository.ApplyStashAsync(new(
            resolution.RootPath,
            new(command.ExpectedFingerprint.Value),
            DeveloperGitStashOperation.Create,
            ExpectedStashCommitSha: null,
            command.Message.Value,
            command.IncludeUntracked), cancellationToken);
        return MapStashes(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitStashInspectionResult> ApplyStashAsync(
        DeveloperGitStashApplyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], null, resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, [], null, "git_stashes_goal_context_denied",
                "Developer stash management is available only in the original workspace.");
        DeveloperGitStashResult result = await repository.ApplyStashAsync(new(
            resolution.RootPath,
            new(command.ExpectedFingerprint.Value),
            DeveloperGitStashOperation.Apply,
            command.Stash.Value,
            Message: null,
            IncludeUntracked: false), cancellationToken);
        return MapStashes(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitStashDropPreviewResult> PreviewStashDropAsync(
        DeveloperGitStashDropPreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DeveloperGitStashInspectionResult inspection = await InspectStashesAsync(
            command.Workspace, cancellationToken);
        if (inspection.Error is not null || inspection.State is null)
            return new(null, inspection, inspection.ErrorCode, inspection.Error);
        if (!inspection.State.Fingerprint.Equals(command.ExpectedFingerprint.Value,
                StringComparison.Ordinal))
            return new(null, inspection, "git_state_stale",
                "Git references or working state changed after display. Refresh and retry.");
        DeveloperGitStashView? stash = inspection.Stashes.SingleOrDefault(candidate =>
            candidate.CommitSha.Equals(command.Stash));
        if (stash is null)
            return new(null, inspection, "git_stash_missing",
                "The selected stash changed or no longer exists. Refresh and retry.");
        string identity = string.Join('\0', inspection.Context.WorkspaceId.Value,
            inspection.State.Fingerprint, stash.CommitSha.Value, stash.BaseSha,
            stash.Selector, stash.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var preview = new DeveloperGitStashDropPreviewView(
            new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()),
            inspection.Context,
            new(inspection.State.Fingerprint),
            stash,
            $"The stash '{stash.Selector}' at {stash.CommitSha.Value} will be removed from the stash list.",
            "Its commit may remain as an unreachable Git object until pruning, but Harness does not guarantee recovery.",
            HasGuaranteedRecovery: false);
        return new(preview, inspection, null, null);
    }

    public async ValueTask<DeveloperGitStashInspectionResult> ApplyStashDropAsync(
        DeveloperGitStashDropPreviewView preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DeveloperGitStashDropPreviewResult current = await PreviewStashDropAsync(new(
            new(preview.Context.WorkspaceId, GoalId: null), preview.Fingerprint,
            preview.Stash.CommitSha), cancellationToken);
        if (current.Preview is null || current.Error is not null) return current.Inspection;
        if (!current.Preview.Id.Equals(preview.Id))
            return current.Inspection with
            {
                ErrorCode = "git_stash_drop_preview_stale",
                Error = "The stash deletion preview changed. Review a new preview before deleting.",
            };
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(preview.Context.WorkspaceId, GoalId: null), cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], null, resolution.ErrorCode, resolution.Error);
        DeveloperGitStashResult result = await repository.ApplyStashAsync(new(
            resolution.RootPath,
            new(preview.Fingerprint.Value),
            DeveloperGitStashOperation.Drop,
            preview.Stash.CommitSha.Value,
            Message: null,
            IncludeUntracked: false), cancellationToken);
        return MapStashes(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitHistoryPageView> InspectHistoryAsync(
        DeveloperGitHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, request.Path, [], null,
                resolution.ErrorCode, resolution.Error);
        DeveloperGitHistoryPage page = await Task.Run(async () =>
            await repository.InspectHistoryAsync(new(
                resolution.RootPath,
                request.Path is null ? null : new Harness.DataAccess.Inspection.DeveloperGitPath(
                    request.Path.Value),
                request.Cursor is null ? null : new Harness.DataAccess.Inspection.DeveloperGitHistoryCursor(
                    request.Cursor.Value),
                request.MaximumResults), cancellationToken), cancellationToken);
        return new(resolution.Context, page.State is null ? null : Map(page.State), request.Path,
            page.Commits.Select(commit => new DeveloperGitHistoryCommitView(
                new(commit.Sha.Value),
                commit.Parents.Select(parent => new DeveloperGitCommitSha(parent.Value)).ToArray(),
                commit.AuthorName, commit.AuthoredAt, commit.Subject, commit.References)).ToArray(),
            page.NextCursor is null ? null : new(page.NextCursor.Value), page.ErrorCode, page.Error);
    }

    public async ValueTask<DeveloperGitCommitDetailResult> InspectCommitAsync(
        WorkbenchWorkspaceRequest workspace,
        DeveloperGitCommitSha commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(commit);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null, resolution.ErrorCode, resolution.Error);
        Harness.DataAccess.Inspection.DeveloperGitCommitDetailResult result = await Task.Run(async () =>
            await repository.InspectCommitAsync(
                resolution.RootPath, new(commit.Value), cancellationToken), cancellationToken);
        DeveloperGitCommitDetailView? detail = result.Detail is null ? null : new(
            new(result.Detail.Sha.Value),
            result.Detail.Parents.Select(parent => new DeveloperGitCommitSha(parent.Value)).ToArray(),
            result.Detail.AuthorName, result.Detail.AuthorEmail, result.Detail.AuthoredAt,
            result.Detail.CommitterName, result.Detail.CommitterEmail, result.Detail.CommittedAt,
            result.Detail.Message, result.Detail.MessageIsTruncated, result.Detail.References,
            result.Detail.ParentDiffs.Select(diff => new DeveloperGitCommitParentDiffView(
                diff.Parent is null ? null : new(diff.Parent.Value),
                diff.Paths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
                diff.Patch, diff.IsTruncated)).ToArray());
        return new(resolution.Context, result.State is null ? null : Map(result.State),
            detail, result.ErrorCode, result.Error);
    }

    public async ValueTask<DeveloperGitBlamePageView> InspectBlameAsync(
        DeveloperGitBlameRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, request.Path, [], null,
                resolution.ErrorCode, resolution.Error);
        DeveloperGitBlamePage page = await Task.Run(async () =>
            await repository.InspectBlameAsync(new(
                resolution.RootPath, new(request.Path.Value), request.StartLine, request.MaximumLines),
                cancellationToken), cancellationToken);
        return new(resolution.Context, page.State is null ? null : Map(page.State), request.Path,
            page.Lines.Select(line => new DeveloperGitBlameLineView(
                line.LineNumber, new(line.Commit.Value), line.AuthorName, line.AuthoredAt,
                new(line.OriginalPath.Value), line.OriginalLineNumber, line.Text)).ToArray(),
            page.NextStartLine, page.ErrorCode, page.Error);
    }

    public async ValueTask<DeveloperGitConflictInspectionResult> InspectConflictsAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], false,
                resolution.ErrorCode, resolution.Error);
        Harness.DataAccess.Inspection.DeveloperGitConflictInspection inspection =
            await Task.Run(async () => await repository.InspectConflictsAsync(
                resolution.RootPath, cancellationToken), cancellationToken);
        return new(resolution.Context,
            inspection.State is null ? null : Map(inspection.State),
            inspection.Conflicts.Select(MapConflictSummary).ToArray(),
            inspection.IsTruncated,
            inspection.ErrorCode,
            inspection.Error);
    }

    public async ValueTask<DeveloperGitConflictDocumentResult> InspectConflictAsync(
        WorkbenchWorkspaceRequest workspace,
        DeveloperGitPath path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(path);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null,
                resolution.ErrorCode, resolution.Error);
        Harness.DataAccess.Inspection.DeveloperGitConflictDocumentResult result =
            await Task.Run(async () => await repository.InspectConflictAsync(
                resolution.RootPath,
                new Harness.DataAccess.Inspection.DeveloperGitPath(path.Value),
                cancellationToken), cancellationToken);
        return MapConflictDocument(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitConflictDocumentResult> SaveConflictResultAsync(
        DeveloperGitConflictSaveCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null,
                resolution.ErrorCode, resolution.Error);
        Harness.DataAccess.Inspection.DeveloperGitConflictDocumentResult result =
            await Task.Run(async () => await repository.SaveConflictResultAsync(new(
                resolution.RootPath,
                new Harness.DataAccess.Inspection.DeveloperGitStateFingerprint(
                    command.ExpectedFingerprint.Value),
                new Harness.DataAccess.Inspection.DeveloperGitPath(command.Path.Value),
                new Harness.DataAccess.Inspection.DeveloperGitContentHash(
                    command.ExpectedResultHash.Value),
                command.Result), cancellationToken), cancellationToken);
        return MapConflictDocument(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitIndexCommandResult> StageConflictResultAsync(
        DeveloperGitConflictStageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [],
                resolution.ErrorCode, resolution.Error);
        DeveloperGitIndexResult result = await Task.Run(async () =>
            await repository.StageConflictResultAsync(new(
                resolution.RootPath,
                new Harness.DataAccess.Inspection.DeveloperGitStateFingerprint(
                    command.ExpectedFingerprint.Value),
                new Harness.DataAccess.Inspection.DeveloperGitPath(command.Path.Value),
                new Harness.DataAccess.Inspection.DeveloperGitContentHash(
                    command.ExpectedResultHash.Value)), cancellationToken), cancellationToken);
        return new(resolution.Context,
            result.State is null ? null : Map(result.State),
            result.AffectedPaths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
            result.ErrorCode,
            result.Error);
    }

    private static DeveloperGitBranchInspectionResult MapBranches(
        WorkbenchWorkspaceContext context,
        DeveloperGitBranchInspection inspection) => new(
            context,
            inspection.State is null ? null : Map(inspection.State),
            inspection.Branches.Select(branch => new DeveloperGitBranchView(
                new(branch.Name), branch.TipSha, branch.IsCurrent, branch.IsMergedIntoHead)).ToArray(),
            inspection.ErrorCode,
            inspection.Error);

    private static DeveloperGitBranchInspectionResult MapBranches(
        WorkbenchWorkspaceContext context,
        DeveloperGitBranchResult result) => new(
            context,
            result.State is null ? null : Map(result.State),
            result.Branches.Select(branch => new DeveloperGitBranchView(
                new(branch.Name), branch.TipSha, branch.IsCurrent, branch.IsMergedIntoHead)).ToArray(),
            result.ErrorCode,
            result.Error);

    private static DeveloperGitStashInspectionResult MapStashes(
        WorkbenchWorkspaceContext context,
        DeveloperGitStashInspection inspection) => new(
            context,
            inspection.State is null ? null : Map(inspection.State),
            inspection.Stashes.Select(MapStash).ToArray(),
            null,
            inspection.ErrorCode,
            inspection.Error);

    private static DeveloperGitStashInspectionResult MapStashes(
        WorkbenchWorkspaceContext context,
        DeveloperGitStashResult result) => new(
            context,
            result.State is null ? null : Map(result.State),
            result.Stashes.Select(MapStash).ToArray(),
            result.AppliedStashCommitSha is null ? null : new(result.AppliedStashCommitSha),
            result.ErrorCode,
            result.Error);

    private static DeveloperGitStashView MapStash(
        Harness.DataAccess.Inspection.DeveloperGitStash stash) => new(
            stash.Selector,
            new(stash.CommitSha),
            stash.BaseSha,
            stash.CreatedAt,
            stash.Message,
            stash.MessageIsTruncated);

    private static DeveloperGitConflictSummaryView MapConflictSummary(
        Harness.DataAccess.Inspection.DeveloperGitConflictSummary conflict) => new(
        new(conflict.Path.Value),
        conflict.BaseBlob is null ? null : new(conflict.BaseBlob.Value),
        conflict.OursBlob is null ? null : new(conflict.OursBlob.Value),
        conflict.TheirsBlob is null ? null : new(conflict.TheirsBlob.Value),
        conflict.IsBinary);

    private static DeveloperGitConflictDocumentResult MapConflictDocument(
        WorkbenchWorkspaceContext context,
        Harness.DataAccess.Inspection.DeveloperGitConflictDocumentResult result) => new(
        context,
        result.State is null ? null : Map(result.State),
        result.Document is null ? null : new(
            new(result.Document.Path.Value),
            MapConflictSide(result.Document.Base),
            MapConflictSide(result.Document.Ours),
            MapConflictSide(result.Document.Theirs),
            result.Document.Result,
            new(result.Document.ResultHash.Value),
            result.Document.ResultIsTruncated,
            result.Document.UnresolvedRegions.Select(region => new DeveloperGitConflictRegionView(
                region.StartLine, region.SeparatorLine, region.EndLine,
                region.OursLabel, region.TheirsLabel, region.IsComplete)).ToArray()),
        result.ErrorCode,
        result.Error);

    private static DeveloperGitConflictSideView MapConflictSide(
        Harness.DataAccess.Inspection.DeveloperGitConflictSide side) => new(
        side.Path is null ? null : new(side.Path.Value),
        side.Blob is null ? null : new(side.Blob.Value),
        side.Text,
        side.IsMissing,
        side.IsBinary,
        side.IsTruncated);

    private static DeveloperGitTagInspectionResult MapTags(
        WorkbenchWorkspaceContext context,
        DeveloperGitTagInspection inspection) => new(
            context,
            inspection.State is null ? null : Map(inspection.State),
            inspection.Tags.Select(tag => new DeveloperGitTagView(
                new(tag.Name), tag.TargetSha, tag.IsAnnotated, tag.Message,
                tag.MessageIsTruncated)).ToArray(),
            inspection.ErrorCode,
            inspection.Error);

    private static DeveloperGitTagInspectionResult MapTags(
        WorkbenchWorkspaceContext context,
        DeveloperGitTagResult result) => new(
            context,
            result.State is null ? null : Map(result.State),
            result.Tags.Select(tag => new DeveloperGitTagView(
                new(tag.Name), tag.TargetSha, tag.IsAnnotated, tag.Message,
                tag.MessageIsTruncated)).ToArray(),
            result.ErrorCode,
            result.Error);

    private async ValueTask<DeveloperGitWorktreeInspectionResult> MapWorktreesAsync(
        WorkbenchWorkspaceContext context,
        DeveloperGitWorktreeInspection inspection,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkspaceView> registered = workspaceService is null
            ? [] : await workspaceService.ListAsync(cancellationToken);
        return MapWorktrees(context, inspection.State, inspection.WorktreeFingerprint,
            inspection.Worktrees, inspection.ErrorCode, inspection.Error, registered);
    }

    private async ValueTask<DeveloperGitWorktreeInspectionResult> MapWorktreesAsync(
        WorkbenchWorkspaceContext context,
        DeveloperGitWorktreeResult result,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkspaceView> registered = workspaceService is null
            ? [] : await workspaceService.ListAsync(cancellationToken);
        return MapWorktrees(context, result.State, result.WorktreeFingerprint,
            result.Worktrees, result.ErrorCode, result.Error, registered);
    }

    private static DeveloperGitWorktreeInspectionResult MapWorktrees(
        WorkbenchWorkspaceContext context,
        WorkspaceGitState? state,
        Harness.DataAccess.Inspection.DeveloperGitWorktreeSetFingerprint? fingerprint,
        IReadOnlyList<Harness.DataAccess.Inspection.DeveloperGitWorktree> worktrees,
        string? errorCode,
        string? error,
        IReadOnlyList<WorkspaceView> registered)
    {
        HashSet<string> registeredRoots = registered.Select(workspace => NormalizePath(workspace.RootPath))
            .ToHashSet(StringComparer.Ordinal);
        return new(context,
            state is null ? null : Map(state),
            fingerprint is null ? null : new(fingerprint.Value),
            worktrees.Select(worktree => new DeveloperGitWorktreeView(
                new(worktree.Path),
                worktree.Branch is null ? null : new(worktree.Branch),
                worktree.HeadSha,
                worktree.IsMain,
                worktree.IsLocked,
                worktree.LockReason,
                worktree.IsDirty,
                worktree.HasConflicts,
                worktree.IsHarnessManaged,
                registeredRoots.Contains(NormalizePath(worktree.Path)),
                new(worktree.StateFingerprint.Value))).ToArray(),
            errorCode,
            error);
    }

    private static string NormalizePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                           PathTooLongException)
        { return path; }
    }

    private static bool TryValidateDestructive(
        DeveloperGitDestructiveAction action,
        IReadOnlyList<DeveloperGitPath> requested,
        WorkspaceGitStateView state,
        out string? error)
    {
        if (requested.Count is < 1 or > 500 || requested.Any(path => string.IsNullOrWhiteSpace(path.Value)))
        {
            error = "Select between 1 and 500 Git change paths.";
            return false;
        }
        var changes = state.Changes.ToDictionary(change => change.Path, StringComparer.Ordinal);
        foreach (DeveloperGitPath path in requested)
        {
            if (!changes.TryGetValue(path.Value, out WorkspaceGitFileChangeView? change))
            {
                error = "Every selected path must still be present in the displayed Git changes.";
                return false;
            }
            bool valid = action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
                ? change.IsUnstaged && !change.WorktreeStatus.Contains("NewInWorkdir", StringComparison.Ordinal) &&
                  !change.IsConflicted
                : change.IsUnstaged && !change.IsStaged &&
                  change.WorktreeStatus.Contains("NewInWorkdir", StringComparison.Ordinal) &&
                  !change.IsConflicted;
            if (!valid)
            {
                error = action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
                    ? "Discard accepts only tracked, unstaged, non-conflicted paths."
                    : "Cleanup accepts only exact untracked, unstaged, non-conflicted paths.";
                return false;
            }
        }
        error = null;
        return true;
    }

    private static string PreviewId(
        WorkbenchWorkspaceContext context,
        DeveloperGitStateFingerprint fingerprint,
        DeveloperGitDestructiveAction action,
        IReadOnlyList<DeveloperGitPath> paths)
    {
        string identity = $"{context.WorkspaceId.Value}\0{context.Scope}\0{fingerprint.Value}\0{action}\0" +
                          string.Join('\0', paths.Select(path => path.Value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static WorkspaceGitStateView Map(WorkspaceGitState state) => new(
        state.Branch,
        state.HeadSha,
        state.Changes.Select(change => new WorkspaceGitFileChangeView(
            change.Path,
            change.Status,
            change.IndexStatus,
            change.WorktreeStatus,
            change.IsStaged,
            change.IsUnstaged,
            change.IsConflicted)).ToArray(),
        state.Diff,
        state.IsTruncated,
        state.ErrorCode,
        state.Error,
        state.Fingerprint,
        state.StagedDiff,
        state.UnstagedDiff,
        MapPatchUnits(state.PatchUnits));

    internal static IReadOnlyList<DeveloperGitPatchUnitView> MapPatchUnits(
        IReadOnlyList<DeveloperGitPatchUnit>? units) => (units ?? [])
        .Select(unit => new DeveloperGitPatchUnitView(
            unit.Id,
            new(unit.Path.Value),
            unit.Direction == DeveloperGitPatchDirection.Stage
                ? DeveloperGitIndexAction.Stage
                : DeveloperGitIndexAction.Unstage,
            unit.Kind == Harness.DataAccess.Inspection.DeveloperGitPatchKind.Hunk
                ? DeveloperGitPatchKind.Hunk
                : DeveloperGitPatchKind.Line,
            unit.Label,
            unit.OldLine,
            unit.NewLine,
            unit.Preview))
        .ToArray();
}
