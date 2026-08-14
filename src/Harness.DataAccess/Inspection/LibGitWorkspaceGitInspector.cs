using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed class LibGitWorkspaceGitInspector : IWorkspaceGitInspector
{
    public ValueTask<WorkspaceGitState> InspectAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(workspaceRoot);
        if (repositoryPath is null)
        {
            return ValueTask.FromResult(Failure("repository_missing", "No Git repository was found."));
        }

        try
        {
            using Repository repository = new(repositoryPath);
            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(repository.Info.WorkingDirectory));
            string requestedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            if (!requestedRoot.Equals(root, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(Failure(
                    "repository_mismatch",
                    "The workspace root must be the Git repository root."));
            }

            return ValueTask.FromResult(GitRepositoryStateReader.Read(repository, cancellationToken));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or ArgumentException)
        {
            return ValueTask.FromResult(Failure("repository_failed", exception.Message));
        }
    }

    private static WorkspaceGitState Failure(string code, string error) =>
        new(string.Empty, null, [], string.Empty, IsTruncated: false, code, error);
}
