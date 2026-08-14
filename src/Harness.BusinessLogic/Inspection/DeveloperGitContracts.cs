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
}
