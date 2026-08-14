using System.Diagnostics;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed class LibGitDeveloperGitRepository : IDeveloperGitRepository
{
    private const int PatchUnitIdLength = 64;
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

    public async ValueTask<DeveloperGitIndexResult> ApplyPatchAsync(
        DeveloperGitPatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsPatchUnitId(request.PatchUnitId))
            return Failure("git_patch_invalid", "Select a current Git hunk or line.");

        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
            return Failure("repository_missing", "No Git repository was found.");

        try
        {
            WorkspaceGitState before;
            GitPatchApplicationUnit unit;
            string root;
            using (Repository repository = new(repositoryPath))
            {
                root = NormalizeRoot(repository.Info.WorkingDirectory);
                if (!NormalizeRoot(request.RepositoryRoot).Equals(root, StringComparison.Ordinal))
                    return Failure("repository_mismatch", "The workspace root must be the Git repository root.");
                before = GitRepositoryStateReader.Read(repository, cancellationToken);
                if (!CryptographicEquals(before.Fingerprint, request.ExpectedFingerprint.Value))
                    return new(before, [], "git_state_stale",
                        "Git state changed after it was displayed. Refresh and retry.");
                bool wasDisplayed = (before.PatchUnits ?? []).Any(candidate =>
                    candidate.Id.Equals(request.PatchUnitId, StringComparison.Ordinal));
                if (!wasDisplayed) throw new GitPatchUnitUnavailableException();
                unit = GitPatchUnitParser.Parse(
                           before.StagedDiff,
                           before.UnstagedDiff,
                           before.Fingerprint,
                           stagedDiffTruncated: false,
                           unstagedDiffTruncated: false)
                           .SingleOrDefault(candidate => candidate.Unit.Id.Equals(
                               request.PatchUnitId, StringComparison.Ordinal))
                       ?? throw new GitPatchUnitUnavailableException();
            }

            ProcessStartInfo startInfo = CreatePatchStartInfo(root, unit.ApplyInReverse);
            using Process process = new() { StartInfo = startInfo };
            if (!process.Start()) return Failure("git_patch_failed", "Git could not start the patch operation.");
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try
            {
                await process.StandardInput.WriteAsync(unit.Patch.AsMemory(), cancellationToken);
                await process.StandardInput.DisposeAsync();
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(standardError, standardOutput);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }

            using Repository afterRepository = new(repositoryPath);
            WorkspaceGitState after = GitRepositoryStateReader.Read(afterRepository, CancellationToken.None);
            if (process.ExitCode != 0)
                return new(after, [], "git_patch_rejected",
                    "Git could not apply that selection to the current index. Refresh and review the surrounding changes.");
            return new(after, [unit.Unit.Path], null, null);
        }
        catch (GitPatchUnitUnavailableException)
        {
            return Failure("git_patch_stale", "The selected hunk or line is no longer available. Refresh and retry.");
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            return Failure("git_patch_failed", "Git could not apply the selected change.");
        }
    }

    private static ProcessStartInfo CreatePatchStartInfo(string root, bool reverse)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("apply");
        startInfo.ArgumentList.Add("--cached");
        startInfo.ArgumentList.Add("--recount");
        startInfo.ArgumentList.Add("--unidiff-zero");
        startInfo.ArgumentList.Add("--whitespace=nowarn");
        if (reverse) startInfo.ArgumentList.Add("--reverse");
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["LC_ALL"] = "C";
        return startInfo;
    }

    private static bool IsPatchUnitId(string value) =>
        value.Length == PatchUnitIdLength && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

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

    private sealed class GitPatchUnitUnavailableException : Exception;
}
