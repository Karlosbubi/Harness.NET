using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mutations;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed partial class LibGitDeveloperGitRepository
{
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

    public ValueTask<DeveloperGitWorktreeInspection> InspectWorktreesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(repositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(WorktreeInspectionFailure(
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            if (!NormalizeRoot(repositoryRoot).Equals(
                    NormalizeRoot(repository.Info.WorkingDirectory), StringComparison.Ordinal))
                return ValueTask.FromResult(WorktreeInspectionFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
            DeveloperGitWorktree[] worktrees = MapWorktrees(repository, cancellationToken);
            return ValueTask.FromResult(new DeveloperGitWorktreeInspection(
                state, WorktreeFingerprint(worktrees), worktrees, null, null));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException)
        {
            return ValueTask.FromResult(WorktreeInspectionFailure(
                "git_worktrees_failed", "Git worktrees could not be inspected."));
        }
    }

    public async ValueTask<DeveloperGitWorktreeResult> ApplyWorktreeAsync(
        DeveloperGitWorktreeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        DeveloperGitWorktreeInspection before = await InspectWorktreesAsync(
            request.RepositoryRoot, cancellationToken);
        if (before.State is null || before.WorktreeFingerprint is null || before.Error is not null)
            return new(before.State, before.WorktreeFingerprint, before.Worktrees,
                before.ErrorCode, before.Error);
        if (!CryptographicEquals(before.State.Fingerprint, request.ExpectedFingerprint.Value) ||
            !CryptographicEquals(before.WorktreeFingerprint.Value, request.ExpectedWorktreeFingerprint.Value))
            return new(before.State, before.WorktreeFingerprint, before.Worktrees,
                "git_state_stale",
                "Git references, working state, or linked worktrees changed after display. Refresh and retry.");

        try
        {
            string root = NormalizeRoot(request.RepositoryRoot);
            using (Repository repository = new(root))
            {
                if (repository.Info.CurrentOperation != CurrentOperation.None)
                    return new(before.State, before.WorktreeFingerprint, before.Worktrees,
                        "git_operation_in_progress",
                        "Finish the current Git operation before changing worktrees.");
                string? validation = request.Operation == DeveloperGitWorktreeOperation.Create
                    ? ValidateWorktreeCreate(repository, before.Worktrees, request, out string? target)
                    : ValidateWorktreeRemove(before.Worktrees, request, out target);
                if (validation is not null)
                    return new(before.State, before.WorktreeFingerprint, before.Worktrees,
                        "git_worktree_invalid", validation);

                List<string> arguments = ["worktree", request.Operation == DeveloperGitWorktreeOperation.Create
                    ? "add" : "remove"];
                if (request.Operation == DeveloperGitWorktreeOperation.Create)
                {
                    if (request.NewBranch is not null)
                    {
                        arguments.Add("-b");
                        arguments.Add(request.NewBranch);
                        arguments.Add("--no-track");
                    }
                    arguments.Add(target!);
                    arguments.Add(request.NewBranch is null ? request.ExistingBranch! : "HEAD");
                }
                else
                {
                    if (request.Force) arguments.Add("--force");
                    arguments.Add(target!);
                }

                int exitCode = await RunWorktreeGitAsync(root, arguments, cancellationToken);
                DeveloperGitWorktreeInspection after = await InspectWorktreesAsync(
                    request.RepositoryRoot, CancellationToken.None);
                if (exitCode != 0)
                    return new(after.State, after.WorktreeFingerprint, after.Worktrees,
                        request.Operation == DeveloperGitWorktreeOperation.Create
                            ? "git_worktree_create_rejected" : "git_worktree_remove_rejected",
                        request.Operation == DeveloperGitWorktreeOperation.Create
                            ? "Git could not create the requested worktree. Refresh and review its path and branch."
                            : "Git could not remove the requested worktree. Refresh and review its current state.");
                return new(after.State, after.WorktreeFingerprint, after.Worktrees, null, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException or NotSupportedException)
        {
            DeveloperGitWorktreeInspection after = await InspectWorktreesAsync(
                request.RepositoryRoot, CancellationToken.None);
            return new(after.State, after.WorktreeFingerprint, after.Worktrees,
                "git_worktree_failed", "Git could not apply the worktree operation.");
        }
    }

    public async ValueTask<DeveloperGitStashInspection> InspectStashesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(repositoryRoot);
        if (repositoryPath is null)
            return StashInspectionFailure("repository_missing", "No Git repository was found.");
        try
        {
            using Repository repository = new(repositoryPath);
            string root = NormalizeRoot(repository.Info.WorkingDirectory);
            if (!NormalizeRoot(repositoryRoot).Equals(root, StringComparison.Ordinal))
                return StashInspectionFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root.");
            WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
            DeveloperGitStash[] stashes = await ReadStashesAsync(repository, root, cancellationToken);
            return new(state, stashes, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            return StashInspectionFailure("git_stashes_failed", "Git stashes could not be inspected.");
        }
    }

    public async ValueTask<DeveloperGitStashResult> ApplyStashAsync(
        DeveloperGitStashRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        DeveloperGitStashInspection before = await InspectStashesAsync(
            request.RepositoryRoot, cancellationToken);
        if (before.State is null || before.Error is not null)
            return new(before.State, before.Stashes, null, before.ErrorCode, before.Error);
        if (!CryptographicEquals(before.State.Fingerprint, request.ExpectedFingerprint.Value))
            return new(before.State, before.Stashes, null, "git_state_stale",
                "Git references or working state changed after display. Refresh and retry.");

        DeveloperGitStash? selected = null;
        if (request.Operation is DeveloperGitStashOperation.Apply or DeveloperGitStashOperation.Drop)
        {
            selected = before.Stashes.SingleOrDefault(stash => stash.CommitSha.Equals(
                request.ExpectedStashCommitSha, StringComparison.Ordinal));
            if (selected is null)
                return new(before.State, before.Stashes, null, "git_stash_missing",
                    "The selected stash changed or no longer exists. Refresh and retry.");
        }
        string? message = request.Message?.Trim();
        if (request.Operation == DeveloperGitStashOperation.Create &&
            (string.IsNullOrWhiteSpace(message) || message.Length > 1024))
            return new(before.State, before.Stashes, null, "git_stash_message_invalid",
                "Enter a stash message between 1 and 1,024 characters.");

        try
        {
            string root = NormalizeRoot(request.RepositoryRoot);
            using (Repository repository = new(root))
            {
                if (repository.Info.CurrentOperation != CurrentOperation.None)
                    return new(before.State, before.Stashes, null, "git_operation_in_progress",
                        "Finish the current Git operation before changing stashes.");
                if (before.State.Changes.Any(change => change.IsConflicted))
                    return new(before.State, before.Stashes, null, "git_conflicts_present",
                        "Resolve current Git conflicts before changing stashes.");
                if (request.Operation == DeveloperGitStashOperation.Create)
                {
                    string? authorName = repository.Config.Get<string>("user.name")?.Value?.Trim();
                    string? authorEmail = repository.Config.Get<string>("user.email")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(authorName) || string.IsNullOrWhiteSpace(authorEmail))
                        return new(before.State, before.Stashes, null, "git_identity_missing",
                            "Configure Git user.name and user.email before creating a stash.");
                    StashModifiers modifiers = request.IncludeUntracked
                        ? StashModifiers.IncludeUntracked : StashModifiers.Default;
                    repository.Stashes.Add(
                        new Signature(authorName, authorEmail, DateTimeOffset.UtcNow), message!, modifiers);
                }
            }

            if (request.Operation == DeveloperGitStashOperation.Create)
            {
                DeveloperGitStashInspection created = await InspectStashesAsync(
                    request.RepositoryRoot, CancellationToken.None);
                return new(created.State, created.Stashes, null, created.ErrorCode, created.Error);
            }

            List<string> arguments = request.Operation switch
            {
                DeveloperGitStashOperation.Apply => ["stash", "apply", "--index", selected!.CommitSha],
                DeveloperGitStashOperation.Drop => ["stash", "drop", selected!.Selector],
                _ => throw new InvalidOperationException("Unsupported Git stash operation."),
            };
            int exitCode = await RunGitAsync(root, arguments, cancellationToken);
            DeveloperGitStashInspection after = await InspectStashesAsync(
                request.RepositoryRoot, CancellationToken.None);
            if (exitCode != 0)
            {
                bool conflicts = after.State?.Changes.Any(change => change.IsConflicted) == true;
                return new(after.State, after.Stashes, null,
                    conflicts ? "git_stash_apply_conflict" : "git_stash_rejected",
                    conflicts
                        ? "The stash produced conflicts and was kept. Resolve or abort the working-tree changes before retrying."
                        : "Git rejected the stash operation. Refresh and review the working state.");
            }
            return new(after.State, after.Stashes,
                request.Operation == DeveloperGitStashOperation.Apply ? selected!.CommitSha : null,
                null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            DeveloperGitStashInspection after = await InspectStashesAsync(
                request.RepositoryRoot, CancellationToken.None);
            return new(after.State, after.Stashes, null,
                "git_stash_failed", "Git could not apply the stash operation.");
        }
    }

}
