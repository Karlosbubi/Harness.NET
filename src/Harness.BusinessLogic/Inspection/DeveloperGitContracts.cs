using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Inspection;

public enum DeveloperGitIndexAction
{
    Stage,
    Unstage,
}

public sealed record DeveloperGitStateFingerprint(string Value);
public sealed record DeveloperGitPath(string Value);
public sealed record DeveloperGitIndexCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitIndexAction Action,
    IReadOnlyList<DeveloperGitPath> Paths);
public sealed record DeveloperGitIndexCommandResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceGitStateView? State,
    IReadOnlyList<DeveloperGitPath> AffectedPaths,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitPatchUnitView(
    string Id,
    DeveloperGitPath Path,
    DeveloperGitIndexAction Action,
    DeveloperGitPatchKind Kind,
    string Label,
    int? OldLine,
    int? NewLine,
    string Preview);
public enum DeveloperGitPatchKind
{
    Hunk,
    Line,
}
public enum DeveloperGitDestructiveAction
{
    DiscardTrackedWorktree,
    DeleteUntracked,
}
public sealed record DeveloperGitDestructivePreviewId(string Value);
public sealed record DeveloperGitDestructivePreviewCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitDestructiveAction Action,
    IReadOnlyList<DeveloperGitPath> Paths);
public sealed record DeveloperGitDestructivePreviewView(
    DeveloperGitDestructivePreviewId Id,
    WorkbenchWorkspaceContext Context,
    DeveloperGitStateFingerprint Fingerprint,
    DeveloperGitDestructiveAction Action,
    IReadOnlyList<DeveloperGitPath> Paths,
    string Title,
    string Consequence,
    string Recovery,
    bool HasGuaranteedRecovery);
public sealed record DeveloperGitDestructivePreviewResult(
    DeveloperGitDestructivePreviewView? Preview,
    WorkspaceGitStateView? State,
    string? ErrorCode,
    string? Error);
public enum DeveloperGitCommitAction
{
    Create,
    Amend,
}
public enum DeveloperGitCommitHookPolicy
{
    RunConfiguredHooks,
    BypassHooks,
}
public sealed record DeveloperGitCommitMessage(string Value);
public sealed record DeveloperGitCommitPreviewId(string Value);
public sealed record DeveloperGitCommitPreviewCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitCommitAction Action,
    DeveloperGitCommitHookPolicy HookPolicy,
    DeveloperGitCommitMessage Message);
public sealed record DeveloperGitCommitPreviewView(
    DeveloperGitCommitPreviewId Id,
    WorkbenchWorkspaceContext Context,
    DeveloperGitStateFingerprint Fingerprint,
    DeveloperGitCommitAction Action,
    DeveloperGitCommitHookPolicy HookPolicy,
    DeveloperGitCommitMessage Message,
    string Branch,
    string? HeadSha,
    string AuthorName,
    string AuthorEmail,
    IReadOnlyList<DeveloperGitPath> StagedPaths,
    string StagedDiff,
    string Consequence,
    string Recovery,
    bool HasGuaranteedRecovery);
public sealed record DeveloperGitCommitPreviewResult(
    DeveloperGitCommitPreviewView? Preview,
    WorkspaceGitStateView? State,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitCommitCommandResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceGitStateView? State,
    string? CommitSha,
    string? ErrorCode,
    string? Error);
public enum DeveloperGitBranchAction
{
    Create,
    Switch,
    Rename,
}
public sealed record DeveloperGitBranchName(string Value);
public sealed record DeveloperGitBranchView(
    DeveloperGitBranchName Name,
    string TipSha,
    bool IsCurrent,
    bool IsMergedIntoHead);
public sealed record DeveloperGitBranchInspectionResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceGitStateView? State,
    IReadOnlyList<DeveloperGitBranchView> Branches,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitBranchCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitBranchAction Action,
    DeveloperGitBranchName? ExistingName,
    DeveloperGitBranchName? NewName);
public sealed record DeveloperGitBranchDeletePreviewId(string Value);
public sealed record DeveloperGitBranchDeletePreviewCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitBranchName Name,
    bool Force);
public sealed record DeveloperGitBranchDeletePreviewView(
    DeveloperGitBranchDeletePreviewId Id,
    WorkbenchWorkspaceContext Context,
    DeveloperGitStateFingerprint Fingerprint,
    DeveloperGitBranchView Branch,
    bool Force,
    string Consequence,
    string Recovery,
    bool HasGuaranteedRecovery);
public sealed record DeveloperGitBranchDeletePreviewResult(
    DeveloperGitBranchDeletePreviewView? Preview,
    DeveloperGitBranchInspectionResult Inspection,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitTagName(string Value);
public sealed record DeveloperGitTagMessage(string Value);
public sealed record DeveloperGitTagView(
    DeveloperGitTagName Name,
    string TargetSha,
    bool IsAnnotated,
    string? Message,
    bool MessageIsTruncated);
public sealed record DeveloperGitTagInspectionResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceGitStateView? State,
    IReadOnlyList<DeveloperGitTagView> Tags,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitTagCreateCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitTagName Name,
    bool Annotated,
    DeveloperGitTagMessage? Message);
public sealed record DeveloperGitTagDeletePreviewId(string Value);
public sealed record DeveloperGitTagDeletePreviewCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitTagName Name);
public sealed record DeveloperGitTagDeletePreviewView(
    DeveloperGitTagDeletePreviewId Id,
    WorkbenchWorkspaceContext Context,
    DeveloperGitStateFingerprint Fingerprint,
    DeveloperGitTagView Tag,
    string Consequence,
    string Recovery,
    bool HasGuaranteedRecovery);
public sealed record DeveloperGitTagDeletePreviewResult(
    DeveloperGitTagDeletePreviewView? Preview,
    DeveloperGitTagInspectionResult Inspection,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitWorktreePath(string Value);
public sealed record DeveloperGitWorktreeSetFingerprint(string Value);
public sealed record DeveloperGitWorktreeView(
    DeveloperGitWorktreePath Path,
    DeveloperGitBranchName? Branch,
    string HeadSha,
    bool IsMain,
    bool IsLocked,
    string? LockReason,
    bool IsDirty,
    bool HasConflicts,
    bool IsHarnessManaged,
    bool IsRegisteredWorkspace,
    DeveloperGitStateFingerprint StateFingerprint);
public sealed record DeveloperGitWorktreeInspectionResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceGitStateView? State,
    DeveloperGitWorktreeSetFingerprint? WorktreeFingerprint,
    IReadOnlyList<DeveloperGitWorktreeView> Worktrees,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitWorktreeCreateCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitWorktreeSetFingerprint ExpectedWorktreeFingerprint,
    DeveloperGitWorktreePath Path,
    DeveloperGitBranchName? ExistingBranch,
    DeveloperGitBranchName? NewBranch);
public sealed record DeveloperGitWorktreeRemovePreviewId(string Value);
public sealed record DeveloperGitWorktreeRemovePreviewCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitWorktreeSetFingerprint ExpectedWorktreeFingerprint,
    DeveloperGitWorktreePath Path,
    bool Force);
public sealed record DeveloperGitWorktreeRemovePreviewView(
    DeveloperGitWorktreeRemovePreviewId Id,
    WorkbenchWorkspaceContext Context,
    DeveloperGitStateFingerprint Fingerprint,
    DeveloperGitWorktreeSetFingerprint WorktreeFingerprint,
    DeveloperGitWorktreeView Worktree,
    bool Force,
    string Consequence,
    string Recovery,
    bool HasGuaranteedRecovery);
public sealed record DeveloperGitWorktreeRemovePreviewResult(
    DeveloperGitWorktreeRemovePreviewView? Preview,
    DeveloperGitWorktreeInspectionResult Inspection,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitStashCommitSha(string Value);
public sealed record DeveloperGitStashMessage(string Value);
public sealed record DeveloperGitStashView(
    string Selector,
    DeveloperGitStashCommitSha CommitSha,
    string BaseSha,
    DateTimeOffset CreatedAt,
    string Message,
    bool MessageIsTruncated);
public sealed record DeveloperGitStashInspectionResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceGitStateView? State,
    IReadOnlyList<DeveloperGitStashView> Stashes,
    DeveloperGitStashCommitSha? AppliedStash,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitStashCreateCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitStashMessage Message,
    bool IncludeUntracked);
public sealed record DeveloperGitStashApplyCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitStashCommitSha Stash);
public sealed record DeveloperGitStashDropPreviewId(string Value);
public sealed record DeveloperGitStashDropPreviewCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitStashCommitSha Stash);
public sealed record DeveloperGitStashDropPreviewView(
    DeveloperGitStashDropPreviewId Id,
    WorkbenchWorkspaceContext Context,
    DeveloperGitStateFingerprint Fingerprint,
    DeveloperGitStashView Stash,
    string Consequence,
    string Recovery,
    bool HasGuaranteedRecovery);
public sealed record DeveloperGitStashDropPreviewResult(
    DeveloperGitStashDropPreviewView? Preview,
    DeveloperGitStashInspectionResult Inspection,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitPatchCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    string PatchUnitId);

public interface IDeveloperGitService
{
    ValueTask<DeveloperGitIndexCommandResult> UpdateIndexAsync(
        DeveloperGitIndexCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitIndexCommandResult> ApplyPatchAsync(
        DeveloperGitPatchCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitDestructivePreviewResult> PreviewDestructiveAsync(
        DeveloperGitDestructivePreviewCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitIndexCommandResult> ApplyDestructiveAsync(
        DeveloperGitDestructivePreviewView preview,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitCommitPreviewResult> PreviewCommitAsync(
        DeveloperGitCommitPreviewCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitCommitCommandResult> CommitAsync(
        DeveloperGitCommitPreviewView preview,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitBranchInspectionResult> InspectBranchesAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitBranchInspectionResult> ApplyBranchAsync(
        DeveloperGitBranchCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitBranchDeletePreviewResult> PreviewBranchDeleteAsync(
        DeveloperGitBranchDeletePreviewCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitBranchInspectionResult> ApplyBranchDeleteAsync(
        DeveloperGitBranchDeletePreviewView preview,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitTagInspectionResult> InspectTagsAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitTagInspectionResult> CreateTagAsync(
        DeveloperGitTagCreateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitTagDeletePreviewResult> PreviewTagDeleteAsync(
        DeveloperGitTagDeletePreviewCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitTagInspectionResult> ApplyTagDeleteAsync(
        DeveloperGitTagDeletePreviewView preview,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitWorktreeInspectionResult> InspectWorktreesAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitWorktreeInspectionResult> CreateWorktreeAsync(
        DeveloperGitWorktreeCreateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitWorktreeRemovePreviewResult> PreviewWorktreeRemoveAsync(
        DeveloperGitWorktreeRemovePreviewCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitWorktreeInspectionResult> ApplyWorktreeRemoveAsync(
        DeveloperGitWorktreeRemovePreviewView preview,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitStashInspectionResult> InspectStashesAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitStashInspectionResult> CreateStashAsync(
        DeveloperGitStashCreateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitStashInspectionResult> ApplyStashAsync(
        DeveloperGitStashApplyCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitStashDropPreviewResult> PreviewStashDropAsync(
        DeveloperGitStashDropPreviewCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitStashInspectionResult> ApplyStashDropAsync(
        DeveloperGitStashDropPreviewView preview,
        CancellationToken cancellationToken = default);
}
