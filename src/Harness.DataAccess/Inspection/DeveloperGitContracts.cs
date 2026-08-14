namespace Harness.DataAccess.Inspection;

public enum DeveloperGitIndexOperation
{
    Stage,
    Unstage,
}

public enum DeveloperGitPatchDirection
{
    Stage,
    Unstage,
}

public enum DeveloperGitPatchKind
{
    Hunk,
    Line,
}

public enum DeveloperGitDestructiveOperation
{
    DiscardTrackedWorktree,
    DeleteUntracked,
}

public enum DeveloperGitCommitOperation
{
    Create,
    Amend,
}

public enum DeveloperGitHookPolicy
{
    RunConfiguredHooks,
    BypassHooks,
}

public enum DeveloperGitBranchOperation
{
    Create,
    Switch,
    Rename,
    Delete,
}

public enum DeveloperGitTagOperation
{
    Create,
    Delete,
}

public sealed record DeveloperGitStateFingerprint(string Value);
public sealed record DeveloperGitPath(string Value);
public sealed record DeveloperGitIndexRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitIndexOperation Operation,
    IReadOnlyList<DeveloperGitPath> Paths);
public sealed record DeveloperGitIndexResult(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitPath> AffectedPaths,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitPatchUnit(
    string Id,
    DeveloperGitPath Path,
    DeveloperGitPatchDirection Direction,
    DeveloperGitPatchKind Kind,
    string Label,
    int? OldLine,
    int? NewLine,
    string Preview);
public sealed record DeveloperGitPatchRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    string PatchUnitId);
public sealed record DeveloperGitDestructiveRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitDestructiveOperation Operation,
    IReadOnlyList<DeveloperGitPath> Paths);
public sealed record DeveloperGitCommitIdentity(string Name, string Email);
public sealed record DeveloperGitCommitIdentityResult(
    DeveloperGitCommitIdentity? Identity,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitCommitRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitCommitOperation Operation,
    DeveloperGitHookPolicy HookPolicy,
    string Message);
public sealed record DeveloperGitCommitResult(
    WorkspaceGitState? State,
    string? CommitSha,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitBranch(
    string Name,
    string TipSha,
    bool IsCurrent,
    bool IsMergedIntoHead);
public sealed record DeveloperGitBranchInspection(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitBranch> Branches,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitBranchRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitBranchOperation Operation,
    string? ExistingName,
    string? NewName,
    bool Force);
public sealed record DeveloperGitBranchResult(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitBranch> Branches,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitTag(
    string Name,
    string TargetSha,
    bool IsAnnotated,
    string? Message,
    bool MessageIsTruncated);
public sealed record DeveloperGitTagInspection(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitTag> Tags,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitTagRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitTagOperation Operation,
    string Name,
    bool Annotated,
    string? Message);
public sealed record DeveloperGitTagResult(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitTag> Tags,
    string? ErrorCode,
    string? Error);

public interface IDeveloperGitRepository
{
    ValueTask<DeveloperGitIndexResult> UpdateIndexAsync(
        DeveloperGitIndexRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitIndexResult> ApplyPatchAsync(
        DeveloperGitPatchRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitIndexResult> ApplyDestructiveAsync(
        DeveloperGitDestructiveRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitCommitIdentityResult> GetCommitIdentityAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitCommitResult> CommitAsync(
        DeveloperGitCommitRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitBranchInspection> InspectBranchesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitBranchResult> ApplyBranchAsync(
        DeveloperGitBranchRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitTagInspection> InspectTagsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitTagResult> ApplyTagAsync(
        DeveloperGitTagRequest request,
        CancellationToken cancellationToken = default);
}
