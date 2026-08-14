namespace Harness.DataAccess.Inspection;

public enum DeveloperGitIndexOperation
{
    Stage,
    Unstage,
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

public interface IDeveloperGitRepository
{
    ValueTask<DeveloperGitIndexResult> UpdateIndexAsync(
        DeveloperGitIndexRequest request,
        CancellationToken cancellationToken = default);
}
