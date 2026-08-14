using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed class LibGitDeveloperGitRepository : IDeveloperGitRepository
{
    public ValueTask<DeveloperGitIndexResult> UpdateIndexAsync(
        DeveloperGitIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryValidatePaths(request.RepositoryRoot, request.Paths, out string[] paths,
                out string? validationError))
        {
            return ValueTask.FromResult(Failure("git_paths_invalid", validationError!));
        }

        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
        {
            return ValueTask.FromResult(Failure("repository_missing", "No Git repository was found."));
        }

        try
        {
            using Repository repository = new(repositoryPath);
            string root = NormalizeRoot(repository.Info.WorkingDirectory);
            if (!NormalizeRoot(request.RepositoryRoot).Equals(root, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(Failure(
                    "repository_mismatch",
                    "The workspace root must be the Git repository root."));
            }

            WorkspaceGitState before = GitRepositoryStateReader.Read(repository, cancellationToken);
            if (!CryptographicEquals(before.Fingerprint, request.ExpectedFingerprint.Value))
            {
                return ValueTask.FromResult(new DeveloperGitIndexResult(
                    before,
                    [],
                    "git_state_stale",
                    "Git state changed after it was displayed. Refresh and retry."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (request.Operation == DeveloperGitIndexOperation.Stage)
            {
                Commands.Stage(repository, paths);
            }
            else
            {
                Commands.Unstage(repository, paths);
            }

            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceGitState after = GitRepositoryStateReader.Read(repository, cancellationToken);
            return ValueTask.FromResult(new DeveloperGitIndexResult(
                after,
                paths.Select(path => new DeveloperGitPath(path)).ToArray(),
                null,
                null));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException)
        {
            return ValueTask.FromResult(Failure("git_index_failed", exception.Message));
        }
    }

    private static bool TryValidatePaths(
        string repositoryRoot,
        IReadOnlyList<DeveloperGitPath> requested,
        out string[] paths,
        out string? error)
    {
        paths = [];
        error = null;
        if (string.IsNullOrWhiteSpace(repositoryRoot) || requested.Count is < 1 or > 500)
        {
            error = "Select between 1 and 500 repository paths.";
            return false;
        }

        string root;
        try
        {
            root = NormalizeRoot(repositoryRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "The repository root is invalid.";
            return false;
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (DeveloperGitPath requestedPath in requested)
        {
            string path = requestedPath.Value.Replace('\\', '/').Trim();
            if (path.Length == 0 || Path.IsPathRooted(path) || path.Equals(".git", StringComparison.Ordinal) ||
                path.StartsWith(".git/", StringComparison.Ordinal))
            {
                error = "Every selected path must be a repository-relative file path outside .git.";
                return false;
            }

            string absolute = Path.GetFullPath(path, root);
            string relative = Path.GetRelativePath(root, absolute).Replace('\\', '/');
            if (relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith("../", StringComparison.Ordinal) ||
                !relative.Equals(path, StringComparison.Ordinal) || !unique.Add(relative))
            {
                error = "Selected paths must be distinct canonical paths inside the repository.";
                return false;
            }
        }

        paths = unique.Order(StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool CryptographicEquals(string actual, string expected)
    {
        byte[] left = System.Text.Encoding.UTF8.GetBytes(actual);
        byte[] right = System.Text.Encoding.UTF8.GetBytes(expected ?? string.Empty);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static DeveloperGitIndexResult Failure(string code, string error) =>
        new(null, [], code, error);
}
