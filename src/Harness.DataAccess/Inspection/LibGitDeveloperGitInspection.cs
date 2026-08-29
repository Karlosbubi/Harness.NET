using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mutations;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed partial class LibGitDeveloperGitRepository
{
    public ValueTask<DeveloperGitHistoryPage> InspectHistoryAsync(
        DeveloperGitHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MaximumResults is < 1 or > 200)
            return ValueTask.FromResult(HistoryFailure(request.Path,
                "git_history_page_invalid", "Request between 1 and 200 history entries."));
        string[] paths = [];
        if (request.Path is not null && !TryValidatePaths(request.RepositoryRoot, [request.Path],
                out paths, out string? pathError))
            return ValueTask.FromResult(HistoryFailure(request.Path, "git_history_path_invalid", pathError!));

        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(HistoryFailure(request.Path,
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            string root = NormalizeRoot(repository.Info.WorkingDirectory);
            if (!NormalizeRoot(request.RepositoryRoot).Equals(root, StringComparison.Ordinal))
                return ValueTask.FromResult(HistoryFailure(request.Path,
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
            CommitFilter filter = new()
            {
                IncludeReachableFrom = repository.Refs,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            };
            IEnumerable<Commit> commits = request.Path is null
                ? repository.Commits.QueryBy(filter)
                : repository.Commits.QueryBy(paths[0], filter).Select(entry => entry.Commit);
            bool afterCursor = request.Cursor is null;
            var page = new List<DeveloperGitHistoryCommit>(request.MaximumResults + 1);
            foreach (Commit commit in commits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!afterCursor)
                {
                    if (commit.Sha.Equals(request.Cursor!.Value, StringComparison.Ordinal))
                        afterCursor = true;
                    continue;
                }
                page.Add(MapHistoryCommit(repository, commit));
                if (page.Count > request.MaximumResults) break;
            }
            if (request.Cursor is not null && !afterCursor)
                return ValueTask.FromResult(new DeveloperGitHistoryPage(state, request.Path, [], null,
                    "git_history_cursor_stale", "The history cursor is no longer reachable. Refresh history."));
            bool hasMore = page.Count > request.MaximumResults;
            if (hasMore) page.RemoveAt(page.Count - 1);
            DeveloperGitHistoryCursor? next = hasMore && page.Count > 0
                ? new(page[^1].Sha.Value) : null;
            return ValueTask.FromResult(new DeveloperGitHistoryPage(
                state, request.Path, page, next, null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            return ValueTask.FromResult(HistoryFailure(request.Path,
                "git_history_failed", "Git history could not be inspected."));
        }
    }

    public ValueTask<DeveloperGitCommitDetailResult> InspectCommitAsync(
        string repositoryRoot,
        DeveloperGitCommitSha commit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(repositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(CommitDetailFailure(
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            string root = NormalizeRoot(repository.Info.WorkingDirectory);
            if (!NormalizeRoot(repositoryRoot).Equals(root, StringComparison.Ordinal))
                return ValueTask.FromResult(CommitDetailFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
            Commit? selected = repository.Lookup<Commit>(commit.Value);
            if (selected is null || !selected.Sha.Equals(commit.Value, StringComparison.Ordinal))
                return ValueTask.FromResult(new DeveloperGitCommitDetailResult(
                    state, null, "git_commit_missing", "The selected commit no longer exists."));
            Commit[] parents = selected.Parents.ToArray();
            var diffs = new List<DeveloperGitCommitParentDiff>(Math.Max(1, parents.Length));
            if (parents.Length == 0)
                diffs.Add(MapParentDiff(repository, null, selected, cancellationToken));
            else
                foreach (Commit parent in parents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    diffs.Add(MapParentDiff(repository, parent, selected, cancellationToken));
                }
            const int maximumMessageCharacters = 131_072;
            string message = selected.Message.TrimEnd();
            bool messageIsTruncated = message.Length > maximumMessageCharacters;
            if (messageIsTruncated) message = message[..maximumMessageCharacters];
            var detail = new DeveloperGitCommitDetail(
                new(selected.Sha),
                parents.Select(parent => new DeveloperGitCommitSha(parent.Sha)).ToArray(),
                selected.Author.Name, selected.Author.Email, selected.Author.When,
                selected.Committer.Name, selected.Committer.Email, selected.Committer.When,
                message, messageIsTruncated, ReferencesFor(repository, selected.Sha), diffs);
            return ValueTask.FromResult(new DeveloperGitCommitDetailResult(state, detail, null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            return ValueTask.FromResult(CommitDetailFailure(
                "git_commit_inspection_failed", "The Git commit could not be inspected."));
        }
    }

    public ValueTask<DeveloperGitBlamePage> InspectBlameAsync(
        DeveloperGitBlameRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string[] paths = [];
        string? pathError = null;
        bool requestValid = request.StartLine >= 1 && request.MaximumLines is >= 1 and <= 500 &&
            TryValidatePaths(request.RepositoryRoot, [request.Path], out paths, out pathError);
        if (!requestValid)
            return ValueTask.FromResult(BlameFailure(request.Path, "git_blame_request_invalid",
                pathError ?? "Request a positive start line and between 1 and 500 lines."));
        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(BlameFailure(request.Path,
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            string root = NormalizeRoot(repository.Info.WorkingDirectory);
            if (!NormalizeRoot(request.RepositoryRoot).Equals(root, StringComparison.Ordinal))
                return ValueTask.FromResult(BlameFailure(request.Path,
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
            Commit? head = repository.Head.Tip;
            TreeEntry? entry = head?[paths[0]];
            if (entry?.Target is not Blob blob)
                return ValueTask.FromResult(new DeveloperGitBlamePage(state, request.Path, [], null,
                    "git_blame_path_missing", "The selected path is not a file at HEAD."));
            string normalizedText = blob.GetContentText().Replace("\r\n", "\n", StringComparison.Ordinal);
            string[] text = normalizedText.Length == 0
                ? []
                : normalizedText.EndsWith('\n')
                ? normalizedText[..^1].Split('\n')
                : normalizedText.Split('\n');
            BlameHunkCollection blame = repository.Blame(paths[0], new());
            int endExclusive = Math.Min(text.Length, request.StartLine - 1 + request.MaximumLines);
            var lines = new List<DeveloperGitBlameLine>(Math.Max(0, endExclusive - request.StartLine + 1));
            for (int line = request.StartLine; line <= endExclusive; line++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BlameHunk hunk = blame.HunkForLine(line - 1);
                int originalLine = hunk.InitialStartLineNumber + line - hunk.FinalStartLineNumber;
                lines.Add(new(line, new(hunk.FinalCommit.Sha), hunk.FinalSignature.Name,
                    hunk.FinalSignature.When, new(hunk.InitialPath), originalLine, text[line - 1]));
            }
            int? next = endExclusive < text.Length ? endExclusive + 1 : null;
            return ValueTask.FromResult(new DeveloperGitBlamePage(
                state, request.Path, lines, next, null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            return ValueTask.FromResult(BlameFailure(request.Path,
                "git_blame_failed", "Git blame could not be inspected."));
        }
    }

    public ValueTask<DeveloperGitConflictInspection> InspectConflictsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(repositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(ConflictInspectionFailure(
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            if (!IsRepositoryRoot(repositoryRoot, repository))
                return ValueTask.FromResult(ConflictInspectionFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
            string[] names = repository.Index.Conflicts
                .Select(ConflictPath)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(501)
                .ToArray();
            bool truncated = names.Length > 500;
            DeveloperGitConflictSummary[] conflicts = names.Take(500).Select(name =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Conflict conflict = repository.Index.Conflicts[name];
                return new DeveloperGitConflictSummary(
                    new(name),
                    Sha(conflict.Ancestor),
                    Sha(conflict.Ours),
                    Sha(conflict.Theirs),
                    IsBinary(repository, conflict.Ancestor) ||
                    IsBinary(repository, conflict.Ours) ||
                    IsBinary(repository, conflict.Theirs));
            }).ToArray();
            return ValueTask.FromResult(new DeveloperGitConflictInspection(
                state, conflicts, truncated, null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            return ValueTask.FromResult(ConflictInspectionFailure(
                "git_conflict_inspection_failed", "Git conflicts could not be inspected."));
        }
    }

    public ValueTask<DeveloperGitConflictDocumentResult> InspectConflictAsync(
        string repositoryRoot,
        DeveloperGitPath path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryValidatePaths(repositoryRoot, [path], out string[] paths, out string? pathError))
            return ValueTask.FromResult(ConflictDocumentFailure(
                "git_conflict_path_invalid", pathError!));
        return ValueTask.FromResult(InspectConflict(repositoryRoot, paths[0], cancellationToken));
    }

    public async ValueTask<DeveloperGitConflictDocumentResult> SaveConflictResultAsync(
        DeveloperGitConflictSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(request.Result) > MaximumConflictContentBytes)
            return ConflictDocumentFailure("git_conflict_result_too_large",
                "The merge result cannot exceed 1 MiB.");
        if (!TryValidatePaths(request.RepositoryRoot, [request.Path], out string[] paths,
                out string? pathError))
            return ConflictDocumentFailure("git_conflict_path_invalid", pathError!);
        string? repositoryPath = Repository.Discover(request.RepositoryRoot);
        if (repositoryPath is null)
            return ConflictDocumentFailure("repository_missing", "No Git repository was found.");
        try
        {
            using (Repository repository = new(repositoryPath))
            {
                if (!IsRepositoryRoot(request.RepositoryRoot, repository))
                    return ConflictDocumentFailure("repository_mismatch",
                        "The workspace root must be the Git repository root.");
                WorkspaceGitState before = GitRepositoryStateReader.Read(repository, cancellationToken);
                if (!CryptographicEquals(before.Fingerprint, request.ExpectedFingerprint.Value))
                    return new(before, null, "git_state_stale",
                        "Git state changed after the conflict was displayed. Refresh and retry.");
                if (repository.Index.Conflicts[paths[0]] is null)
                    return new(before, null, "git_conflict_resolved",
                        "The selected path is no longer conflicted.");
            }

            WorkspaceFileEditResult saved = await fileEditor.ApplyAsync(
                request.RepositoryRoot,
                new(paths[0], request.ExpectedResultHash.Value, request.Result),
                cancellationToken);
            if (saved.Error is not null)
                return ConflictDocumentFailure(
                    saved.ErrorCode ?? "git_conflict_save_failed", saved.Error);
            return InspectConflict(request.RepositoryRoot, paths[0], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException)
        {
            return ConflictDocumentFailure(
                "git_conflict_save_failed", "The merge result could not be saved.");
        }
    }

    public async ValueTask<DeveloperGitIndexResult> StageConflictResultAsync(
        DeveloperGitConflictStageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DeveloperGitConflictDocumentResult inspected = await InspectConflictAsync(
            request.RepositoryRoot, request.Path, cancellationToken);
        if (inspected.Document is null || inspected.State is null)
            return new(inspected.State, [], inspected.ErrorCode, inspected.Error);
        if (!CryptographicEquals(inspected.State.Fingerprint, request.ExpectedFingerprint.Value))
            return new(inspected.State, [], "git_state_stale",
                "Git state changed after the merge result was displayed. Refresh and retry.");
        if (!CryptographicEquals(
                inspected.Document.ResultHash.Value, request.ExpectedResultHash.Value))
            return new(inspected.State, [], "content_changed",
                "The merge result no longer matches the displayed content hash.");
        if (inspected.Document.UnresolvedRegions.Count > 0)
            return new(inspected.State, [], "git_conflict_markers_remain",
                "Remove every conflict-marker region and save the result before staging it.");
        return await UpdateIndexAsync(new(
            request.RepositoryRoot,
            request.ExpectedFingerprint,
            DeveloperGitIndexOperation.Stage,
            [request.Path]), cancellationToken);
    }

    public ValueTask<DeveloperGitRemoteInspection> InspectRemotesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? repositoryPath = Repository.Discover(repositoryRoot);
        if (repositoryPath is null)
            return ValueTask.FromResult(RemoteInspectionFailure(
                "repository_missing", "No Git repository was found."));
        try
        {
            using Repository repository = new(repositoryPath);
            if (!IsRepositoryRoot(repositoryRoot, repository))
                return ValueTask.FromResult(RemoteInspectionFailure(
                    "repository_mismatch", "The workspace root must be the Git repository root."));
            WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
            DeveloperGitRemote[] remotes = repository.Network.Remotes
                .OrderBy(remote => remote.Name, StringComparer.Ordinal)
                .Select(remote => new DeveloperGitRemote(
                    new(remote.Name), SanitizeRemoteUrl(remote.Url),
                    remote.FetchRefSpecs.Select(spec => spec.Specification).ToArray(),
                    remote.PushRefSpecs.Select(spec => spec.Specification).ToArray()))
                .ToArray();
            Branch branch = repository.Head;
            Branch? tracked = repository.Info.IsHeadDetached ? null : branch.TrackedBranch;
            string? upstreamRemote = repository.Info.IsHeadDetached
                ? null : branch.RemoteName;
            string? upstreamBranch = tracked?.FriendlyName;
            if (upstreamBranch is not null && upstreamRemote is not null &&
                upstreamBranch.StartsWith(upstreamRemote + "/", StringComparison.Ordinal))
                upstreamBranch = upstreamBranch[(upstreamRemote.Length + 1)..];
            int? ahead = tracked is null ? null : branch.TrackingDetails.AheadBy;
            int? behind = tracked is null ? null : branch.TrackingDetails.BehindBy;
            return ValueTask.FromResult(new DeveloperGitRemoteInspection(
                state, remotes,
                repository.Info.IsHeadDetached ? null : new(branch.FriendlyName),
                upstreamRemote is null ? null : new(upstreamRemote),
                upstreamBranch is null ? null : new(upstreamBranch),
                branch.Tip?.Sha, tracked?.Tip?.Sha,
                ahead, behind,
                null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
                                           UnauthorizedAccessException or ArgumentException or
                                           InvalidOperationException or UriFormatException)
        {
            return ValueTask.FromResult(RemoteInspectionFailure(
                "git_remote_inspection_failed", "Git remotes could not be inspected."));
        }
    }

    public async ValueTask<DeveloperGitRemoteResult> ApplyRemoteAsync(
        DeveloperGitRemoteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DeveloperGitRemoteInspection before = await InspectRemotesAsync(
            request.RepositoryRoot, cancellationToken);
        if (before.State is null || before.Error is not null)
            return new(before, before.ErrorCode, before.Error);
        if (!CryptographicEquals(before.State.Fingerprint, request.ExpectedFingerprint.Value))
            return new(before, "git_state_stale",
                "Git references or working state changed after display. Refresh and retry.");
        if (!before.Remotes.Any(candidate => candidate.Name == request.Remote))
            return new(before, "git_remote_missing", "The selected remote no longer exists.");
        if (!ValidBranchName(request.Source.Value) || !ValidBranchName(request.Destination.Value))
            return new(before, "git_remote_reference_invalid",
                "Remote synchronization accepts explicit local branch names only.");
        if (!string.Equals(before.LocalSha, request.ExpectedLocalSha, StringComparison.Ordinal) ||
            !string.Equals(before.RemoteTrackingSha, request.ExpectedRemoteTrackingSha,
                StringComparison.Ordinal))
            return new(before, "git_remote_observation_stale",
                "The displayed local or remote-tracking commit changed. Refresh and retry.");
        if ((request.Operation is DeveloperGitRemoteOperation.PullMerge or
             DeveloperGitRemoteOperation.PullRebase) && before.State.Changes.Count > 0)
            return new(before, "git_pull_dirty",
                "Commit or stash working changes before integrating fetched commits.");
        if (request.Operation == DeveloperGitRemoteOperation.Push &&
            request.PushPolicy == DeveloperGitPushPolicy.ForceWithLease &&
            request.ExpectedRemoteTrackingSha is null)
            return new(before, "git_force_lease_unknown",
                "Fetch the destination before using force-with-lease.");

        string root = NormalizeRoot(request.RepositoryRoot);
        List<string> arguments = request.Operation switch
        {
            DeveloperGitRemoteOperation.Fetch =>
            [
                "fetch", "--no-tags", "--", request.Remote.Value,
                $"+refs/heads/{request.Source.Value}:refs/remotes/{request.Remote.Value}/{request.Destination.Value}",
            ],
            DeveloperGitRemoteOperation.PullMerge =>
            ["merge", "--ff-only", $"refs/remotes/{request.Remote.Value}/{request.Destination.Value}"],
            DeveloperGitRemoteOperation.PullRebase =>
            ["rebase", $"refs/remotes/{request.Remote.Value}/{request.Destination.Value}"],
            DeveloperGitRemoteOperation.Push =>
            ["push", "--", request.Remote.Value,
                $"refs/heads/{request.Source.Value}:refs/heads/{request.Destination.Value}"],
            _ => throw new InvalidOperationException("Unsupported Git remote operation."),
        };
        if (request.Operation == DeveloperGitRemoteOperation.Push &&
            request.PushPolicy == DeveloperGitPushPolicy.ForceWithLease)
            arguments.Insert(1,
                $"--force-with-lease=refs/heads/{request.Destination.Value}:{request.ExpectedRemoteTrackingSha}");
        try
        {
            int exitCode = await RunGitAsync(root, arguments, cancellationToken);
            DeveloperGitRemoteInspection after = await InspectRemotesAsync(
                request.RepositoryRoot, CancellationToken.None);
            if (exitCode != 0)
                return new(after, RemoteFailureCode(request.Operation), RemoteFailureMessage(request.Operation));
            return new(after, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or InvalidOperationException)
        {
            DeveloperGitRemoteInspection after = await InspectRemotesAsync(
                request.RepositoryRoot, CancellationToken.None);
            return new(after, RemoteFailureCode(request.Operation), RemoteFailureMessage(request.Operation));
        }
    }

}
