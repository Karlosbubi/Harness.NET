using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Inspection;

internal sealed partial class DeveloperGitService
{
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

}
