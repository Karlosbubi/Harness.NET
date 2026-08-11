using System.Text;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed class LibGitWorkspaceGitInspector : IWorkspaceGitInspector
{
    private const int MaximumChanges = 500;
    private const int MaximumDiffBytes = 128 * 1024;

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

            StatusEntry[] statusEntries = repository.RetrieveStatus()
                .OrderBy(entry => entry.FilePath, StringComparer.Ordinal)
                .ToArray();
            WorkspaceGitFileChange[] changes = statusEntries
                .Take(MaximumChanges)
                .Select(entry => new WorkspaceGitFileChange(
                    entry.FilePath,
                    entry.State.ToString()))
                .ToArray();
            bool isTruncated = statusEntries.Length > MaximumChanges;
            string diff = string.Empty;
            if (repository.Head.Tip is not null)
            {
                string[] trackedChangePaths = statusEntries
                    .Where(entry =>
                        (entry.State & FileStatus.NewInWorkdir) == 0 &&
                        (entry.State & FileStatus.Ignored) == 0)
                    .Select(entry => entry.FilePath)
                    .ToArray();
                if (trackedChangePaths.Length > 0)
                {
                    using Patch patch = repository.Diff.Compare<Patch>(
                        repository.Head.Tip.Tree,
                        DiffTargets.Index | DiffTargets.WorkingDirectory,
                        trackedChangePaths);
                    diff = BoundUtf8(patch.Content, out bool diffTruncated);
                    isTruncated |= diffTruncated;
                }
            }

            return ValueTask.FromResult(new WorkspaceGitState(
                repository.Head.FriendlyName,
                repository.Head.Tip?.Sha,
                changes,
                diff,
                isTruncated,
                ErrorCode: null,
                Error: null));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or ArgumentException)
        {
            return ValueTask.FromResult(Failure("repository_failed", exception.Message));
        }
    }

    private static string BoundUtf8(string content, out bool isTruncated)
    {
        int byteCount = Encoding.UTF8.GetByteCount(content);
        isTruncated = byteCount > MaximumDiffBytes;
        if (!isTruncated)
        {
            return content;
        }

        byte[] buffer = new byte[MaximumDiffBytes];
        Encoder encoder = Encoding.UTF8.GetEncoder();
        encoder.Convert(
            content.AsSpan(),
            buffer.AsSpan(),
            flush: false,
            out _,
            out int bytesUsed,
            out _);
        return Encoding.UTF8.GetString(buffer, 0, bytesUsed);
    }

    private static WorkspaceGitState Failure(string code, string error) =>
        new(string.Empty, null, [], string.Empty, IsTruncated: false, code, error);
}
