using System.Text;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed class GitWorkspaceTextSearcher : IWorkspaceTextSearcher
{
    private const int MaximumFiles = 10_000;
    private const int MaximumFileBytes = 1024 * 1024;
    private const int MaximumMatches = 100;
    private const int MaximumSnippetCharacters = 500;
    private const int MaximumQueryCharacters = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async ValueTask<WorkspaceTextSearch> SearchAsync(
        string workspaceRoot,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > MaximumQueryCharacters)
        {
            return Failure("invalid_query", $"The search query must contain 1-{MaximumQueryCharacters} characters.");
        }

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
                return Failure("repository_mismatch", "The workspace root must be the Git repository root.");
            }

            List<WorkspaceTextMatch> matches = [];
            int filesScanned = 0;
            bool isTruncated = false;
            foreach (string relativePath in repository.Index
                         .Select(entry => entry.Path)
                         .Distinct(StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (filesScanned >= MaximumFiles)
                {
                    isTruncated = true;
                    break;
                }

                if (!WorkspacePathPolicy.TryResolve(
                        root,
                        relativePath,
                        out _,
                        out string confinedPath,
                        out string targetPath,
                        out _,
                        out _))
                {
                    continue;
                }

                FileInfo file = new(targetPath);
                if (!file.Exists || file.Length > MaximumFileBytes)
                {
                    continue;
                }

                filesScanned++;
                await SearchFileAsync(
                    file.FullName,
                    confinedPath,
                    query,
                    matches,
                    cancellationToken);
                if (matches.Count >= MaximumMatches)
                {
                    isTruncated = true;
                    break;
                }
            }

            return new(matches, filesScanned, isTruncated, ErrorCode: null, Error: null);
        }
        catch (LibGit2SharpException exception)
        {
            return Failure("repository_failed", exception.Message);
        }
    }

    private static async ValueTask SearchFileAsync(
        string fullPath,
        string relativePath,
        string query,
        ICollection<WorkspaceTextMatch> matches,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using StreamReader reader = new(
                stream,
                StrictUtf8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            int lineNumber = 0;
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lineNumber++;
                if (!line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string snippet = line.Length <= MaximumSnippetCharacters
                    ? line
                    : line[..MaximumSnippetCharacters];
                matches.Add(new(relativePath, lineNumber, snippet));
                if (matches.Count >= MaximumMatches)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is DecoderFallbackException or IOException or UnauthorizedAccessException)
        {
            // A single unreadable or non-text tracked file must not fail the repository search.
        }
    }

    private static WorkspaceTextSearch Failure(string code, string error) =>
        new([], 0, IsTruncated: false, code, error);
}
