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
}
