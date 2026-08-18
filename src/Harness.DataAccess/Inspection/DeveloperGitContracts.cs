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

public enum DeveloperGitWorktreeOperation
{
    Create,
    Remove,
}

public enum DeveloperGitStashOperation
{
    Create,
    Apply,
    Drop,
}

public enum DeveloperGitRemoteOperation
{
    Fetch,
    PullMerge,
    PullRebase,
    Push,
}

public enum DeveloperGitPushPolicy
{
    FastForwardOnly,
    ForceWithLease,
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
public sealed record DeveloperGitWorktreeSetFingerprint(string Value);
public sealed record DeveloperGitWorktree(
    string Path,
    string? Branch,
    string HeadSha,
    bool IsMain,
    bool IsLocked,
    string? LockReason,
    bool IsDirty,
    bool HasConflicts,
    bool IsHarnessManaged,
    DeveloperGitStateFingerprint StateFingerprint);
public sealed record DeveloperGitWorktreeInspection(
    WorkspaceGitState? State,
    DeveloperGitWorktreeSetFingerprint? WorktreeFingerprint,
    IReadOnlyList<DeveloperGitWorktree> Worktrees,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitWorktreeRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitWorktreeSetFingerprint ExpectedWorktreeFingerprint,
    DeveloperGitWorktreeOperation Operation,
    string Path,
    string? ExistingBranch,
    string? NewBranch,
    DeveloperGitStateFingerprint? ExpectedSelectedWorktreeFingerprint,
    bool Force);
public sealed record DeveloperGitWorktreeResult(
    WorkspaceGitState? State,
    DeveloperGitWorktreeSetFingerprint? WorktreeFingerprint,
    IReadOnlyList<DeveloperGitWorktree> Worktrees,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitStash(
    string Selector,
    string CommitSha,
    string BaseSha,
    DateTimeOffset CreatedAt,
    string Message,
    bool MessageIsTruncated);
public sealed record DeveloperGitStashInspection(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitStash> Stashes,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitStashRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitStashOperation Operation,
    string? ExpectedStashCommitSha,
    string? Message,
    bool IncludeUntracked);
public sealed record DeveloperGitStashResult(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitStash> Stashes,
    string? AppliedStashCommitSha,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitCommitSha(string Value);
public sealed record DeveloperGitHistoryCursor(string Value);
public sealed record DeveloperGitHistoryRequest(
    string RepositoryRoot,
    DeveloperGitPath? Path,
    DeveloperGitHistoryCursor? Cursor,
    int MaximumResults);
public sealed record DeveloperGitHistoryCommit(
    DeveloperGitCommitSha Sha,
    IReadOnlyList<DeveloperGitCommitSha> Parents,
    string AuthorName,
    DateTimeOffset AuthoredAt,
    string Subject,
    IReadOnlyList<string> References);
public sealed record DeveloperGitHistoryPage(
    WorkspaceGitState? State,
    DeveloperGitPath? Path,
    IReadOnlyList<DeveloperGitHistoryCommit> Commits,
    DeveloperGitHistoryCursor? NextCursor,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitCommitParentDiff(
    DeveloperGitCommitSha? Parent,
    IReadOnlyList<DeveloperGitPath> Paths,
    string Patch,
    bool IsTruncated);
public sealed record DeveloperGitCommitDetail(
    DeveloperGitCommitSha Sha,
    IReadOnlyList<DeveloperGitCommitSha> Parents,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string CommitterName,
    string CommitterEmail,
    DateTimeOffset CommittedAt,
    string Message,
    bool MessageIsTruncated,
    IReadOnlyList<string> References,
    IReadOnlyList<DeveloperGitCommitParentDiff> ParentDiffs);
public sealed record DeveloperGitCommitDetailResult(
    WorkspaceGitState? State,
    DeveloperGitCommitDetail? Detail,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitBlameRequest(
    string RepositoryRoot,
    DeveloperGitPath Path,
    int StartLine,
    int MaximumLines);
public sealed record DeveloperGitBlameLine(
    int LineNumber,
    DeveloperGitCommitSha Commit,
    string AuthorName,
    DateTimeOffset AuthoredAt,
    DeveloperGitPath OriginalPath,
    int OriginalLineNumber,
    string Text);
public sealed record DeveloperGitBlamePage(
    WorkspaceGitState? State,
    DeveloperGitPath Path,
    IReadOnlyList<DeveloperGitBlameLine> Lines,
    int? NextStartLine,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitContentHash(string Value);
public sealed record DeveloperGitConflictSide(
    DeveloperGitPath? Path,
    DeveloperGitCommitSha? Blob,
    string? Text,
    bool IsMissing,
    bool IsBinary,
    bool IsTruncated);
public sealed record DeveloperGitConflictRegion(
    int StartLine,
    int? SeparatorLine,
    int? EndLine,
    string OursLabel,
    string TheirsLabel,
    bool IsComplete);
public sealed record DeveloperGitConflictSummary(
    DeveloperGitPath Path,
    DeveloperGitCommitSha? BaseBlob,
    DeveloperGitCommitSha? OursBlob,
    DeveloperGitCommitSha? TheirsBlob,
    bool IsBinary);
public sealed record DeveloperGitConflictInspection(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitConflictSummary> Conflicts,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitConflictDocument(
    DeveloperGitPath Path,
    DeveloperGitConflictSide Base,
    DeveloperGitConflictSide Ours,
    DeveloperGitConflictSide Theirs,
    string Result,
    DeveloperGitContentHash ResultHash,
    bool ResultIsTruncated,
    IReadOnlyList<DeveloperGitConflictRegion> UnresolvedRegions);
public sealed record DeveloperGitConflictDocumentResult(
    WorkspaceGitState? State,
    DeveloperGitConflictDocument? Document,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitConflictSaveRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitPath Path,
    DeveloperGitContentHash ExpectedResultHash,
    string Result);
public sealed record DeveloperGitConflictStageRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitPath Path,
    DeveloperGitContentHash ExpectedResultHash);
public sealed record DeveloperGitRemoteName(string Value);
public sealed record DeveloperGitReferenceName(string Value);
public sealed record DeveloperGitRemote(
    DeveloperGitRemoteName Name,
    string SanitizedUrl,
    IReadOnlyList<string> FetchRefspecs,
    IReadOnlyList<string> PushRefspecs);
public sealed record DeveloperGitRemoteInspection(
    WorkspaceGitState? State,
    IReadOnlyList<DeveloperGitRemote> Remotes,
    DeveloperGitReferenceName? LocalBranch,
    DeveloperGitRemoteName? UpstreamRemote,
    DeveloperGitReferenceName? UpstreamBranch,
    string? LocalSha,
    string? RemoteTrackingSha,
    int? Ahead,
    int? Behind,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitRemoteRequest(
    string RepositoryRoot,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitRemoteOperation Operation,
    DeveloperGitRemoteName Remote,
    DeveloperGitReferenceName Source,
    DeveloperGitReferenceName Destination,
    string? ExpectedLocalSha,
    string? ExpectedRemoteTrackingSha,
    DeveloperGitPushPolicy PushPolicy);
public sealed record DeveloperGitRemoteResult(
    DeveloperGitRemoteInspection Inspection,
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

    ValueTask<DeveloperGitWorktreeInspection> InspectWorktreesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitWorktreeResult> ApplyWorktreeAsync(
        DeveloperGitWorktreeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitStashInspection> InspectStashesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitStashResult> ApplyStashAsync(
        DeveloperGitStashRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitHistoryPage> InspectHistoryAsync(
        DeveloperGitHistoryRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitCommitDetailResult> InspectCommitAsync(
        string repositoryRoot,
        DeveloperGitCommitSha commit,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitBlamePage> InspectBlameAsync(
        DeveloperGitBlameRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitConflictInspection> InspectConflictsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitConflictDocumentResult> InspectConflictAsync(
        string repositoryRoot,
        DeveloperGitPath path,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitConflictDocumentResult> SaveConflictResultAsync(
        DeveloperGitConflictSaveRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitIndexResult> StageConflictResultAsync(
        DeveloperGitConflictStageRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitRemoteInspection> InspectRemotesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitRemoteResult> ApplyRemoteAsync(
        DeveloperGitRemoteRequest request,
        CancellationToken cancellationToken = default);
}
