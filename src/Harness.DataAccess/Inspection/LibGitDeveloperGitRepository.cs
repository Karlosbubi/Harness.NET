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

    public ValueTask<DeveloperGitBranchInspection> InspectBranchesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(repositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(BranchInspectionFailure(
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            if (!NormalizeRoot(repositoryRoot).Equals(
                    NormalizeRoot(repository.Info.WorkingDirectory), StringComparison.Ordinal))
                return ValueTask.FromResult(BranchInspectionFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
            return ValueTask.FromResult(new DeveloperGitBranchInspection(
                state, MapBranches(repository), null, null));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException)
        {
            return ValueTask.FromResult(BranchInspectionFailure(
                "git_branches_failed", "Git branches could not be inspected."));
        }
    }

    public ValueTask<DeveloperGitBranchResult> ApplyBranchAsync(
        DeveloperGitBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(BranchFailure(
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            if (!NormalizeRoot(request.RepositoryRoot).Equals(
                    NormalizeRoot(repository.Info.WorkingDirectory), StringComparison.Ordinal))
                return ValueTask.FromResult(BranchFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState before = GitRepositoryStateReader.Read(repository, cancellationToken);
            if (!CryptographicEquals(before.Fingerprint, request.ExpectedFingerprint.Value))
                return ValueTask.FromResult(new DeveloperGitBranchResult(
                    before, MapBranches(repository), "git_state_stale",
                    "Git references or working state changed after they were displayed. Refresh and retry."));
            if (repository.Info.CurrentOperation != CurrentOperation.None)
                return ValueTask.FromResult(new DeveloperGitBranchResult(
                    before, MapBranches(repository), "git_operation_in_progress",
                    "Finish the current Git operation before changing branches."));

            string? validation = ValidateBranchRequest(repository, request);
            if (validation is not null)
                return ValueTask.FromResult(new DeveloperGitBranchResult(
                    before, MapBranches(repository), "git_branch_invalid", validation));
            switch (request.Operation)
            {
                case DeveloperGitBranchOperation.Create:
                    repository.CreateBranch(request.NewName!);
                    break;
                case DeveloperGitBranchOperation.Switch:
                    Commands.Checkout(repository, repository.Branches[request.ExistingName!]!);
                    break;
                case DeveloperGitBranchOperation.Rename:
                    repository.Branches.Rename(request.ExistingName!, request.NewName!);
                    break;
                case DeveloperGitBranchOperation.Delete:
                    repository.Branches.Remove(request.ExistingName!);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported Git branch operation.");
            }
            WorkspaceGitState after = GitRepositoryStateReader.Read(repository, cancellationToken);
            return ValueTask.FromResult(new DeveloperGitBranchResult(
                after, MapBranches(repository), null, null));
        }
        catch (CheckoutConflictException)
        {
            return ValueTask.FromResult(ReadBranchFailure(repositoryPath,
                "git_branch_checkout_conflict",
                "Local changes conflict with that branch. Commit, stash, or discard them before switching."));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return ValueTask.FromResult(ReadBranchFailure(repositoryPath,
                "git_branch_failed", "Git could not apply the branch operation."));
        }
    }

    public ValueTask<DeveloperGitTagInspection> InspectTagsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(repositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(TagInspectionFailure(
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            if (!NormalizeRoot(repositoryRoot).Equals(
                    NormalizeRoot(repository.Info.WorkingDirectory), StringComparison.Ordinal))
                return ValueTask.FromResult(TagInspectionFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            return ValueTask.FromResult(new DeveloperGitTagInspection(
                GitRepositoryStateReader.Read(repository, cancellationToken),
                MapTags(repository), null, null));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException)
        {
            return ValueTask.FromResult(TagInspectionFailure(
                "git_tags_failed", "Git tags could not be inspected."));
        }
    }

    public ValueTask<DeveloperGitTagResult> ApplyTagAsync(
        DeveloperGitTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(TagFailure("repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            if (!NormalizeRoot(request.RepositoryRoot).Equals(
                    NormalizeRoot(repository.Info.WorkingDirectory), StringComparison.Ordinal))
                return ValueTask.FromResult(TagFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState before = GitRepositoryStateReader.Read(repository, cancellationToken);
            if (!CryptographicEquals(before.Fingerprint, request.ExpectedFingerprint.Value))
                return ValueTask.FromResult(new DeveloperGitTagResult(
                    before, MapTags(repository), "git_state_stale",
                    "Git references or working state changed after they were displayed. Refresh and retry."));
            if (repository.Info.CurrentOperation != CurrentOperation.None)
                return ValueTask.FromResult(new DeveloperGitTagResult(
                    before, MapTags(repository), "git_operation_in_progress",
                    "Finish the current Git operation before changing tags."));
            string name = request.Name.Trim();
            if (name.Length == 0 || !Reference.IsValidName($"refs/tags/{name}"))
                return ValueTask.FromResult(new DeveloperGitTagResult(
                    before, MapTags(repository), "git_tag_invalid", "Enter a valid local tag name."));
            Tag? existing = repository.Tags[name];
            if (request.Operation == DeveloperGitTagOperation.Create)
            {
                if (existing is not null)
                    return ValueTask.FromResult(new DeveloperGitTagResult(
                        before, MapTags(repository), "git_tag_exists", "A tag with that name already exists."));
                if (repository.Head.Tip is null)
                    return ValueTask.FromResult(new DeveloperGitTagResult(
                        before, MapTags(repository), "git_tag_unborn", "An unborn branch has no commit to tag."));
                if (request.Annotated)
                {
                    string? message = request.Message?.Trim();
                    if (string.IsNullOrWhiteSpace(message) || message.Length > 32_768)
                        return ValueTask.FromResult(new DeveloperGitTagResult(
                            before, MapTags(repository), "git_tag_message_invalid",
                            "Enter an annotated tag message between 1 and 32,768 characters."));
                    string? authorName = repository.Config.Get<string>("user.name")?.Value?.Trim();
                    string? authorEmail = repository.Config.Get<string>("user.email")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(authorName) || string.IsNullOrWhiteSpace(authorEmail))
                        return ValueTask.FromResult(new DeveloperGitTagResult(
                            before, MapTags(repository), "git_identity_missing",
                            "Configure Git user.name and user.email before creating an annotated tag."));
                    repository.ApplyTag(name, repository.Head.Tip.Sha,
                        new Signature(authorName, authorEmail, DateTimeOffset.UtcNow), message);
                }
                else
                {
                    repository.ApplyTag(name, repository.Head.Tip.Sha);
                }
            }
            else
            {
                if (existing is null)
                    return ValueTask.FromResult(new DeveloperGitTagResult(
                        before, MapTags(repository), "git_tag_missing", "Select an existing local tag."));
                repository.Tags.Remove(name);
            }
            WorkspaceGitState after = GitRepositoryStateReader.Read(repository, cancellationToken);
            return ValueTask.FromResult(new DeveloperGitTagResult(after, MapTags(repository), null, null));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return ValueTask.FromResult(ReadTagFailure(repositoryPath,
                "git_tag_failed", "Git could not apply the tag operation."));
        }
    }

    private static DeveloperGitTag[] MapTags(Repository repository) =>
        repository.Tags.OrderBy(tag => tag.FriendlyName, StringComparer.Ordinal)
            .Select(MapTag)
            .ToArray();

    private static DeveloperGitTag MapTag(Tag tag)
    {
        const int maximumDisplayedMessageCharacters = 32_768;
        string? message = tag.Annotation?.Message?.TrimEnd();
        bool truncated = message?.Length > maximumDisplayedMessageCharacters;
        if (truncated)
            message = message![..maximumDisplayedMessageCharacters];
        return new(tag.FriendlyName, tag.Target.Peel<Commit>()?.Sha ?? tag.Target.Sha,
            tag.Annotation is not null, message, truncated);
    }

    private static DeveloperGitTagResult ReadTagFailure(string repositoryPath, string code, string error)
    {
        try
        {
            using Repository repository = new(repositoryPath);
            return new(GitRepositoryStateReader.Read(repository, CancellationToken.None),
                MapTags(repository), code, error);
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException)
        {
            return TagFailure(code, error);
        }
    }

    private static DeveloperGitBranchResult ReadBranchFailure(
        string repositoryPath,
        string code,
        string error)
    {
        try
        {
            using Repository repository = new(repositoryPath);
            return new(GitRepositoryStateReader.Read(repository, CancellationToken.None),
                MapBranches(repository), code, error);
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException)
        {
            return BranchFailure(code, error);
        }
    }

    private static string? ValidateBranchRequest(Repository repository, DeveloperGitBranchRequest request)
    {
        bool needsExisting = request.Operation is DeveloperGitBranchOperation.Switch or
            DeveloperGitBranchOperation.Rename or DeveloperGitBranchOperation.Delete;
        bool needsNew = request.Operation is DeveloperGitBranchOperation.Create or
            DeveloperGitBranchOperation.Rename;
        Branch? existing = needsExisting && !string.IsNullOrWhiteSpace(request.ExistingName)
            ? repository.Branches[request.ExistingName] : null;
        if (needsExisting && (existing is null || existing.IsRemote))
            return "Select an existing local branch.";
        if (needsNew && (string.IsNullOrWhiteSpace(request.NewName) ||
                         !Reference.IsValidName($"refs/heads/{request.NewName}")))
            return "Enter a valid new local branch name.";
        if (needsNew && repository.Branches[request.NewName] is not null)
            return "A branch with that name already exists.";
        if (request.Operation == DeveloperGitBranchOperation.Switch && existing!.IsCurrentRepositoryHead)
            return "That branch is already checked out.";
        if (request.Operation == DeveloperGitBranchOperation.Delete)
        {
            if (existing!.IsCurrentRepositoryHead) return "The current branch cannot be deleted.";
            if (!request.Force && !IsMergedIntoHead(repository, existing))
                return "The branch is not merged into HEAD. Review and explicitly choose force deletion.";
        }
        return null;
    }

    private static DeveloperGitBranch[] MapBranches(Repository repository) =>
        repository.Branches.Where(branch => !branch.IsRemote)
            .OrderBy(branch => branch.FriendlyName, StringComparer.Ordinal)
            .Select(branch => new DeveloperGitBranch(
                branch.FriendlyName,
                branch.Tip?.Sha ?? string.Empty,
                branch.IsCurrentRepositoryHead,
                IsMergedIntoHead(repository, branch)))
            .ToArray();

    private static bool IsMergedIntoHead(Repository repository, Branch branch)
    {
        if (repository.Head.Tip is null || branch.Tip is null) return false;
        Commit? mergeBase = repository.ObjectDatabase.FindMergeBase(repository.Head.Tip, branch.Tip);
        return mergeBase?.Sha == branch.Tip.Sha;
    }

    private static bool ValidateDestructiveSelection(
        WorkspaceGitState state,
        DeveloperGitDestructiveOperation operation,
        IReadOnlyList<string> paths,
        out string? error)
    {
        var changes = state.Changes.ToDictionary(change => change.Path, StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!changes.TryGetValue(path, out WorkspaceGitFileChange? change))
            {
                error = "Every selected path must still be present in the displayed Git changes.";
                return false;
            }

            bool valid = operation == DeveloperGitDestructiveOperation.DiscardTrackedWorktree
                ? change.IsUnstaged && !change.WorktreeStatus.Contains("NewInWorkdir", StringComparison.Ordinal) &&
                  !change.IsConflicted
                : change.IsUnstaged && !change.IsStaged &&
                  change.WorktreeStatus.Contains("NewInWorkdir", StringComparison.Ordinal) &&
                  !change.IsConflicted;
            if (!valid)
            {
                error = operation == DeveloperGitDestructiveOperation.DiscardTrackedWorktree
                    ? "Discard accepts only tracked, unstaged, non-conflicted paths."
                    : "Cleanup accepts only exact untracked, unstaged, non-conflicted paths.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static async Task<int> RunRestoreAsync(
        string root,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreateGitStartInfo(root);
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add("--worktree");
        startInfo.ArgumentList.Add("--");
        foreach (string path in paths) startInfo.ArgumentList.Add(path);
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start()) return -1;
        Task standardError = DrainAsync(process.StandardError, cancellationToken);
        Task standardOutput = DrainAsync(process.StandardOutput, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardError, standardOutput);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<int> RunCommitAsync(
        string root,
        DeveloperGitCommitRequest request,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreateGitStartInfo(root);
        startInfo.RedirectStandardInput = true;
        startInfo.ArgumentList.Add("commit");
        startInfo.ArgumentList.Add("--file=-");
        if (request.Operation == DeveloperGitCommitOperation.Amend)
            startInfo.ArgumentList.Add("--amend");
        if (request.HookPolicy == DeveloperGitHookPolicy.BypassHooks)
            startInfo.ArgumentList.Add("--no-verify");
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start()) return -1;
        Task standardError = DrainAsync(process.StandardError, cancellationToken);
        Task standardOutput = DrainAsync(process.StandardOutput, cancellationToken);
        await process.StandardInput.WriteAsync(request.Message.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardError, standardOutput);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private static ProcessStartInfo CreatePatchStartInfo(string root, bool reverse)
    {
        ProcessStartInfo startInfo = CreateGitStartInfo(root);
        startInfo.RedirectStandardInput = true;
        startInfo.ArgumentList.Add("apply");
        startInfo.ArgumentList.Add("--cached");
        startInfo.ArgumentList.Add("--recount");
        startInfo.ArgumentList.Add("--unidiff-zero");
        startInfo.ArgumentList.Add("--whitespace=nowarn");
        if (reverse) startInfo.ArgumentList.Add("--reverse");
        return startInfo;
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) > 0)
        {
            // Git and hooks may emit arbitrarily large output. Drain it without retaining it;
            // developer-facing failures remain bounded and sanitized.
        }
    }

    private static ProcessStartInfo CreateGitStartInfo(string root)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
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

    private static DeveloperGitCommitResult CommitFailure(string code, string error) =>
        new(null, null, code, error);

    private static DeveloperGitBranchInspection BranchInspectionFailure(string code, string error) =>
        new(null, [], code, error);

    private static DeveloperGitBranchResult BranchFailure(string code, string error) =>
        new(null, [], code, error);

    private static DeveloperGitTagInspection TagInspectionFailure(string code, string error) =>
        new(null, [], code, error);

    private static DeveloperGitTagResult TagFailure(string code, string error) =>
        new(null, [], code, error);

    private sealed class GitPatchUnitUnavailableException : Exception;
}
