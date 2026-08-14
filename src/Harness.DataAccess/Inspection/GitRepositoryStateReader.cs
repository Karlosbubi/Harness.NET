using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal static class GitRepositoryStateReader
{
    internal const int MaximumChanges = 500;
    private const int MaximumDiffBytes = 128 * 1024;

    internal static WorkspaceGitState Read(
        Repository repository,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StatusEntry[] entries = repository.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
            IncludeIgnored = false,
        })
            .OrderBy(entry => entry.FilePath, StringComparer.Ordinal)
            .ToArray();
        WorkspaceGitFileChange[] changes = entries.Take(MaximumChanges)
            .Select(Map)
            .ToArray();
        bool truncated = entries.Length > MaximumChanges;
        string staged = Diff(repository, DiffTargets.Index, entries, out bool stagedTruncated);
        string unstaged = UnstagedDiff(repository, entries, out bool unstagedTruncated);
        truncated |= stagedTruncated || unstagedTruncated;
        string combined = Diff(repository, DiffTargets.Index | DiffTargets.WorkingDirectory,
            entries, out bool combinedTruncated);
        truncated |= combinedTruncated;
        string fingerprint = Fingerprint(repository, entries, cancellationToken);
        IReadOnlyList<DeveloperGitPatchUnit> patchUnits = GitPatchUnitParser.Parse(
                staged, unstaged, fingerprint, stagedTruncated, unstagedTruncated)
            .Select(application => application.Unit)
            .ToArray();
        return new(
            repository.Info.IsHeadDetached ? "(detached)" : repository.Head.FriendlyName,
            repository.Head.Tip?.Sha,
            changes,
            combined,
            truncated,
            null,
            null,
            fingerprint,
            staged,
            unstaged,
            patchUnits);
    }

    private static WorkspaceGitFileChange Map(StatusEntry entry)
    {
        FileStatus index = entry.State & (
            FileStatus.NewInIndex | FileStatus.ModifiedInIndex | FileStatus.DeletedFromIndex |
            FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex);
        FileStatus worktree = entry.State & (
            FileStatus.NewInWorkdir | FileStatus.ModifiedInWorkdir | FileStatus.DeletedFromWorkdir |
            FileStatus.RenamedInWorkdir | FileStatus.TypeChangeInWorkdir);
        bool conflict = (entry.State & FileStatus.Conflicted) != 0;
        return new(
            entry.FilePath,
            entry.State.ToString(),
            index.ToString(),
            worktree.ToString(),
            index != FileStatus.Unaltered,
            worktree != FileStatus.Unaltered,
            conflict);
    }

    private static string Diff(
        Repository repository,
        DiffTargets target,
        IReadOnlyList<StatusEntry> entries,
        out bool truncated)
    {
        truncated = false;
        string[] paths = entries
            .Where(entry => ShouldIncludeInDiff(entry.State))
            .Select(entry => entry.FilePath)
            .ToArray();
        if (paths.Length == 0) return string.Empty;
        using Patch patch = repository.Diff.Compare<Patch>(repository.Head.Tip?.Tree, target, paths);
        return BoundUtf8(patch.Content, out truncated);
    }

    private static bool ShouldIncludeInDiff(FileStatus state)
    {
        if ((state & FileStatus.Ignored) != 0) return false;
        bool untrackedOnly = (state & FileStatus.NewInWorkdir) != 0 &&
                             (state & (FileStatus.NewInIndex | FileStatus.ModifiedInIndex |
                                       FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex)) == 0;
        return !untrackedOnly;
    }

    private static string UnstagedDiff(
        Repository repository,
        IReadOnlyList<StatusEntry> entries,
        out bool truncated)
    {
        string[] paths = entries
            .Where(entry => (entry.State & (FileStatus.ModifiedInWorkdir |
                                            FileStatus.DeletedFromWorkdir |
                                            FileStatus.RenamedInWorkdir |
                                            FileStatus.TypeChangeInWorkdir)) != 0)
            .Select(entry => entry.FilePath)
            .ToArray();
        if (paths.Length == 0)
        {
            truncated = false;
            return string.Empty;
        }

        using Patch patch = repository.Diff.Compare<Patch>(paths, includeUntracked: false);
        return BoundUtf8(patch.Content, out truncated);
    }

    private static string Fingerprint(
        Repository repository,
        IReadOnlyList<StatusEntry> entries,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, repository.Head.Tip?.Sha ?? "unborn");
        Append(hash, repository.Info.IsHeadDetached ? "detached" : repository.Head.FriendlyName);
        Append(hash, repository.Info.CurrentOperation.ToString());
        string indexPath = Path.Combine(repository.Info.Path, "index");
        AppendFile(hash, indexPath, cancellationToken);
        string root = repository.Info.WorkingDirectory;
        foreach (StatusEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, entry.FilePath);
            Append(hash, ((int)entry.State).ToString(System.Globalization.CultureInfo.InvariantCulture));
            string path = Path.GetFullPath(entry.FilePath, root);
            if (File.Exists(path)) AppendFile(hash, path, cancellationToken);
            else Append(hash, "missing");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendFile(
        IncrementalHash hash,
        string path,
        CancellationToken cancellationToken)
    {
        string? linkTarget = new FileInfo(path).LinkTarget;
        if (linkTarget is not null)
        {
            Append(hash, "symbolic-link");
            Append(hash, linkTarget);
            return;
        }
        if (!File.Exists(path))
        {
            Append(hash, "missing");
            return;
        }
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static string BoundUtf8(string content, out bool isTruncated)
    {
        int byteCount = Encoding.UTF8.GetByteCount(content);
        isTruncated = byteCount > MaximumDiffBytes;
        if (!isTruncated) return content;
        byte[] buffer = new byte[MaximumDiffBytes];
        Encoding.UTF8.GetEncoder().Convert(content.AsSpan(), buffer.AsSpan(), false,
            out _, out int used, out _);
        return Encoding.UTF8.GetString(buffer, 0, used);
    }
}
