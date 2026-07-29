using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed class GitWorkspaceFileCatalogReader : IWorkspaceFileCatalogReader
{
    private const int MaximumFiles = 20_000;

    public async ValueTask<WorkspaceFileCatalog> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () => Read(workspaceRoot, cancellationToken),
            cancellationToken);
    }

    private static WorkspaceFileCatalog Read(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(workspaceRoot);
        if (repositoryPath is null)
        {
            return Failure("repository_missing", "No Git repository was found.");
        }

        try
        {
            using Repository repository = new(repositoryPath);
            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(repository.Info.WorkingDirectory));
            string requestedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            if (!requestedRoot.Equals(root, StringComparison.Ordinal))
            {
                return Failure(
                    "repository_mismatch",
                    "The workspace root must be the Git repository root.");
            }

            string[] paths = repository.Index
                .Select(entry => entry.Path.Replace('\\', '/'))
                .Where(path => IsExistingFile(root, path))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return new WorkspaceFileCatalog(
                paths.Take(MaximumFiles).Select(path => new WorkspaceTrackedPath(path)).ToArray(),
                paths.Length > MaximumFiles,
                ErrorCode: null,
                Error: null);
        }
        catch (LibGit2SharpException exception)
        {
            return Failure("repository_failed", exception.Message);
        }
    }

    private static bool IsExistingFile(string root, string path) =>
        WorkspacePathPolicy.TryResolve(
            root,
            path,
            out _,
            out _,
            out string targetPath,
            out _,
            out _) && File.Exists(targetPath);

    private static WorkspaceFileCatalog Failure(string code, string error) =>
        new([], IsTruncated: false, code, error);
}
