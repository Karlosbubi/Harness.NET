using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mutations;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed partial class LibGitDeveloperGitRepository(
    IApplicationPaths? applicationPaths = null,
    IWorkspaceFileEditor? workspaceFileEditor = null)
    : IDeveloperGitRepository
{
    private const int PatchUnitIdLength = 64;
    private const int MaximumConflictContentBytes = 1024 * 1024;
    private readonly IWorkspaceFileEditor fileEditor =
        workspaceFileEditor ?? new AtomicWorkspaceFileEditor();
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
            return ValueTask.FromResult(Failure("git_index_failed", "Git could not update the selected index paths."));
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
            Task standardError = DrainAsync(process.StandardError, cancellationToken);
            Task standardOutput = DrainAsync(process.StandardOutput, cancellationToken);
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

    public async ValueTask<DeveloperGitIndexResult> ApplyDestructiveAsync(
        DeveloperGitDestructiveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryValidatePaths(request.RepositoryRoot, request.Paths, out string[] paths,
                out string? validationError))
            return Failure("git_paths_invalid", validationError!);
        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
            return Failure("repository_missing", "No Git repository was found.");

        try
        {
            string root;
            WorkspaceGitState before;
            using (Repository repository = new(repositoryPath))
            {
                root = NormalizeRoot(repository.Info.WorkingDirectory);
                if (!NormalizeRoot(request.RepositoryRoot).Equals(root, StringComparison.Ordinal))
                    return Failure("repository_mismatch", "The workspace root must be the Git repository root.");
                before = GitRepositoryStateReader.Read(repository, cancellationToken);
                if (!CryptographicEquals(before.Fingerprint, request.ExpectedFingerprint.Value))
                    return new(before, [], "git_state_stale",
                        "Git state changed after it was displayed. Refresh and retry.");
                if (!ValidateDestructiveSelection(before, request.Operation, paths, out string? error))
                    return new(before, [], "git_destructive_invalid", error);
            }

            if (request.Operation == DeveloperGitDestructiveOperation.DiscardTrackedWorktree)
            {
                int exitCode = await RunRestoreAsync(root, paths, cancellationToken);
                if (exitCode != 0)
                    return Failure("git_discard_failed", "Git could not restore the selected working-tree paths.");
            }
            else
            {
                foreach (string path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string absolute = Path.GetFullPath(path, root);
                    FileAttributes attributes = File.GetAttributes(absolute);
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    bool link = (attributes & FileAttributes.ReparsePoint) != 0;
                    if (directory && !link)
                        return Failure("git_clean_directory_unsupported",
                            "Select the untracked files inside the directory; recursive directory cleanup is not enabled.");
                    if (directory) Directory.Delete(absolute);
                    else File.Delete(absolute);
                }
            }

            using Repository afterRepository = new(repositoryPath);
            WorkspaceGitState after = GitRepositoryStateReader.Read(afterRepository, CancellationToken.None);
            return new(after, paths.Select(path => new DeveloperGitPath(path)).ToArray(), null, null);
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            return Failure("git_destructive_failed", "Git could not apply the selected destructive action.");
        }
    }

    public ValueTask<DeveloperGitCommitIdentityResult> GetCommitIdentityAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string? repositoryPath = Repository.Discover(repositoryRoot);
            if (repositoryPath is null)
                return ValueTask.FromResult(new DeveloperGitCommitIdentityResult(
                    null, "repository_missing", "No Git repository was found."));
            using Repository repository = new(repositoryPath);
            if (!NormalizeRoot(repositoryRoot).Equals(
                    NormalizeRoot(repository.Info.WorkingDirectory), StringComparison.Ordinal))
                return ValueTask.FromResult(new DeveloperGitCommitIdentityResult(
                    null, "repository_mismatch", "The workspace root must be the Git repository root."));
            string? name = repository.Config.Get<string>("user.name")?.Value?.Trim();
            string? email = repository.Config.Get<string>("user.email")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
                return ValueTask.FromResult(new DeveloperGitCommitIdentityResult(
                    null, "git_identity_missing",
                    "Configure Git user.name and user.email before committing."));
            return ValueTask.FromResult(new DeveloperGitCommitIdentityResult(
                new(name, email), null, null));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException)
        {
            return ValueTask.FromResult(new DeveloperGitCommitIdentityResult(
                null, "git_identity_failed", "Git commit identity could not be read."));
        }
    }

    public async ValueTask<DeveloperGitCommitResult> CommitAsync(
        DeveloperGitCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 32_768)
            return CommitFailure("git_commit_message_invalid",
                "Enter a commit message between 1 and 32,768 characters.");
        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
            return CommitFailure("repository_missing", "No Git repository was found.");
        try
        {
            string root;
            using (Repository repository = new(repositoryPath))
            {
                root = NormalizeRoot(repository.Info.WorkingDirectory);
                if (!NormalizeRoot(request.RepositoryRoot).Equals(root, StringComparison.Ordinal))
                    return CommitFailure("repository_mismatch",
                        "The workspace root must be the Git repository root.");
                WorkspaceGitState before = GitRepositoryStateReader.Read(repository, cancellationToken);
                if (!CryptographicEquals(before.Fingerprint, request.ExpectedFingerprint.Value))
                    return new(before, null, "git_state_stale",
                        "Git state changed after the commit preview. Refresh and retry.");
                if (before.Changes.Any(change => change.IsConflicted))
                    return new(before, null, "git_conflicts_present",
                        "Resolve every Git conflict before committing.");
                if (!before.Changes.Any(change => change.IsStaged))
                    return new(before, null, "git_nothing_staged", "Stage at least one change before committing.");
                if (request.Operation == DeveloperGitCommitOperation.Amend && repository.Head.Tip is null)
                    return new(before, null, "git_amend_unborn", "An unborn branch has no commit to amend.");
            }

            int exitCode = await RunCommitAsync(root, request, cancellationToken);
            using Repository afterRepository = new(repositoryPath);
            WorkspaceGitState after = GitRepositoryStateReader.Read(afterRepository, CancellationToken.None);
            if (exitCode != 0)
                return new(after, null, "git_commit_rejected",
                    "Git rejected the commit. Review configured hooks and repository state.");
            return new(after, afterRepository.Head.Tip?.Sha, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return CommitFailure("git_commit_failed", "Git could not create the commit.");
        }
    }


}
