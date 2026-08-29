using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mutations;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed partial class LibGitDeveloperGitRepository
{
    private static bool ValidBranchName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 1024 &&
        Reference.IsValidName($"refs/heads/{value}");

    private static string RemoteFailureCode(DeveloperGitRemoteOperation operation) => operation switch
    {
        DeveloperGitRemoteOperation.Fetch => "git_fetch_failed",
        DeveloperGitRemoteOperation.Push => "git_push_rejected",
        _ => "git_pull_integration_failed",
    };

    private static string RemoteFailureMessage(DeveloperGitRemoteOperation operation) => operation switch
    {
        DeveloperGitRemoteOperation.Fetch =>
            "Fetch failed or credentials were unavailable. Remote output was discarded to protect credential data.",
        DeveloperGitRemoteOperation.Push =>
            "Push was rejected or credentials were unavailable. Refresh remote state before retrying.",
        _ => "Fetched commits could not be integrated. Inspect the repository state before retrying.",
    };

    private static DeveloperGitRemoteInspection RemoteInspectionFailure(string code, string error) =>
        new(null, [], null, null, null, null, null, null, null, code, error);

    private static string SanitizeRemoteUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return Bound(value, 2048) ?? string.Empty;
        if (uri.Scheme is not ("http" or "https"))
            return Bound($"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}", 2048) ?? string.Empty;
        UriBuilder builder = new(uri) { UserName = string.Empty, Password = string.Empty,
            Query = string.Empty, Fragment = string.Empty };
        return Bound(builder.Uri.ToString(), 2048) ?? string.Empty;
    }

    private static DeveloperGitConflictDocumentResult InspectConflict(
        string repositoryRoot,
        string path,
        CancellationToken cancellationToken)
    {
        string? repositoryPath = Repository.Discover(repositoryRoot);
        if (repositoryPath is null)
            return ConflictDocumentFailure("repository_missing", "No Git repository was found.");
        using Repository repository = new(repositoryPath);
        if (!IsRepositoryRoot(repositoryRoot, repository))
            return ConflictDocumentFailure(
                "repository_mismatch", "The workspace root must be the Git repository root.");
        WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
        Conflict? conflict = repository.Index.Conflicts[path];
        if (conflict is null)
            return new(state, null, "git_conflict_missing", "The selected path is not conflicted.");
        string absolute = Path.Combine(NormalizeRoot(repository.Info.WorkingDirectory), path);
        if (!File.Exists(absolute))
            return new(state, null, "git_conflict_result_missing",
                "The conflicted working result does not exist.");
        FileInfo file = new(absolute);
        if (file.Length > MaximumConflictContentBytes)
            return new(state, null, "git_conflict_result_too_large",
                "The conflicted working result exceeds 1 MiB.");
        byte[] bytes = File.ReadAllBytes(absolute);
        string result = new UTF8Encoding(false, true).GetString(bytes);
        var document = new DeveloperGitConflictDocument(
            new(path),
            MapConflictSide(repository, conflict.Ancestor),
            MapConflictSide(repository, conflict.Ours),
            MapConflictSide(repository, conflict.Theirs),
            result,
            new(Convert.ToHexStringLower(SHA256.HashData(bytes))),
            ResultIsTruncated: false,
            FindConflictRegions(result));
        return new(state, document, null, null);
    }

    private static DeveloperGitHistoryCommit MapHistoryCommit(Repository repository, Commit commit)
    {
        const int maximumSubjectLength = 1024;
        string subject = commit.MessageShort.Trim();
        if (subject.Length > maximumSubjectLength) subject = subject[..maximumSubjectLength];
        return new(new(commit.Sha),
            commit.Parents.Select(parent => new DeveloperGitCommitSha(parent.Sha)).ToArray(),
            commit.Author.Name, commit.Author.When, subject, ReferencesFor(repository, commit.Sha));
    }

    private static bool IsRepositoryRoot(string requestedRoot, Repository repository) =>
        NormalizeRoot(requestedRoot).Equals(
            NormalizeRoot(repository.Info.WorkingDirectory), StringComparison.Ordinal);

    private static string ConflictPath(Conflict conflict) =>
        conflict.Ours?.Path ?? conflict.Theirs?.Path ?? conflict.Ancestor?.Path ??
        throw new InvalidOperationException("A Git conflict has no index path.");

    private static DeveloperGitCommitSha? Sha(IndexEntry? entry) =>
        entry is null ? null : new(entry.Id.Sha);

    private static bool IsBinary(Repository repository, IndexEntry? entry) =>
        entry is not null && repository.Lookup<Blob>(entry.Id)?.IsBinary == true;

    private static DeveloperGitConflictSide MapConflictSide(
        Repository repository,
        IndexEntry? entry)
    {
        if (entry is null)
            return new(null, null, null, IsMissing: true, IsBinary: false, IsTruncated: false);
        Blob? blob = repository.Lookup<Blob>(entry.Id);
        if (blob is null)
            return new(new(entry.Path), new(entry.Id.Sha), null,
                IsMissing: false, IsBinary: false, IsTruncated: true);
        bool binary = blob.IsBinary;
        bool truncated = blob.Size > MaximumConflictContentBytes;
        string? text = binary || truncated ? null : blob.GetContentText();
        return new(new(entry.Path), new(entry.Id.Sha), text,
            IsMissing: false, binary, truncated);
    }

    private static DeveloperGitConflictRegion[] FindConflictRegions(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var regions = new List<DeveloperGitConflictRegion>();
        for (int index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("<<<<<<<", StringComparison.Ordinal)) continue;
            int start = index;
            int separator = -1;
            int end = -1;
            for (int candidate = index + 1; candidate < lines.Length; candidate++)
            {
                if (separator < 0 && lines[candidate].StartsWith("=======", StringComparison.Ordinal))
                    separator = candidate;
                else if (lines[candidate].StartsWith(">>>>>>>", StringComparison.Ordinal))
                {
                    end = candidate;
                    break;
                }
            }
            string ours = Bound(lines[start][7..].Trim(), 256) ?? "ours";
            string theirs = end >= 0 ? Bound(lines[end][7..].Trim(), 256) ?? "theirs" : "theirs";
            regions.Add(new(start + 1,
                separator < 0 ? null : separator + 1,
                end < 0 ? null : end + 1,
                ours,
                theirs,
                separator >= 0 && end >= 0));
            if (end >= 0) index = end;
        }
        return [.. regions];
    }

    private static string[] ReferencesFor(Repository repository, string sha) =>
        repository.Branches.Where(branch => branch.Tip?.Sha.Equals(sha, StringComparison.Ordinal) == true)
            .Select(branch => branch.FriendlyName)
            .Concat(repository.Tags.Where(tag =>
                    (tag.Target.Peel<Commit>()?.Sha ?? tag.Target.Sha).Equals(sha, StringComparison.Ordinal))
                .Select(tag => $"tag: {tag.FriendlyName}"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static DeveloperGitCommitParentDiff MapParentDiff(
        Repository repository,
        Commit? parent,
        Commit commit,
        CancellationToken cancellationToken)
    {
        const int maximumPatchCharacters = 1_000_000;
        cancellationToken.ThrowIfCancellationRequested();
        using Patch patch = repository.Diff.Compare<Patch>(parent?.Tree, commit.Tree);
        string content = patch.Content;
        bool truncated = content.Length > maximumPatchCharacters;
        if (truncated) content = content[..maximumPatchCharacters];
        return new(parent is null ? null : new(parent.Sha),
            patch.Select(change => new DeveloperGitPath(change.Path))
                .Distinct().OrderBy(path => path.Value, StringComparer.Ordinal).ToArray(),
            content, truncated);
    }

    private static DeveloperGitHistoryPage HistoryFailure(
        DeveloperGitPath? path, string code, string error) => new(null, path, [], null, code, error);
    private static DeveloperGitCommitDetailResult CommitDetailFailure(string code, string error) =>
        new(null, null, code, error);
    private static DeveloperGitBlamePage BlameFailure(
        DeveloperGitPath path, string code, string error) => new(null, path, [], null, code, error);
    private static DeveloperGitConflictInspection ConflictInspectionFailure(
        string code, string error) => new(null, [], false, code, error);
    private static DeveloperGitConflictDocumentResult ConflictDocumentFailure(
        string code, string error) => new(null, null, code, error);

    private static string? ValidateWorktreeCreate(
        Repository repository,
        IReadOnlyList<DeveloperGitWorktree> worktrees,
        DeveloperGitWorktreeRequest request,
        out string? target)
    {
        string? normalizedTarget = NormalizeWorktreePath(request.Path);
        target = normalizedTarget;
        if (normalizedTarget is null) return "Choose an absolute worktree path.";
        if (worktrees.Any(worktree => IsAtOrBelow(normalizedTarget, worktree.Path)))
            return "The new worktree path must not be inside an existing worktree.";
        if (File.Exists(target)) return "The new worktree path must not be a file.";
        if (Directory.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                return "The new worktree path must not be a symbolic link.";
            if (Directory.EnumerateFileSystemEntries(target).Any())
                return "The new worktree directory must be empty.";
        }
        else if (!Directory.Exists(Path.GetDirectoryName(target)))
        {
            return "The parent directory for the new worktree must exist.";
        }

        bool hasExisting = !string.IsNullOrWhiteSpace(request.ExistingBranch);
        bool hasNew = !string.IsNullOrWhiteSpace(request.NewBranch);
        if (hasExisting == hasNew)
            return "Choose exactly one existing branch or new branch name.";
        if (hasNew)
        {
            if (!Reference.IsValidName($"refs/heads/{request.NewBranch}") ||
                repository.Branches[request.NewBranch] is not null)
                return "Enter a valid unused local branch name.";
            if (repository.Head.Tip is null) return "An unborn repository cannot create a worktree branch.";
        }
        else
        {
            Branch? branch = repository.Branches[request.ExistingBranch];
            if (branch is null || branch.IsRemote) return "Select an existing local branch.";
            if (worktrees.Any(worktree => worktree.Branch?.Equals(
                    request.ExistingBranch, StringComparison.Ordinal) == true))
                return "That local branch is already checked out in another worktree.";
        }
        return null;
    }

    private static string? ValidateWorktreeRemove(
        IReadOnlyList<DeveloperGitWorktree> worktrees,
        DeveloperGitWorktreeRequest request,
        out string? target)
    {
        string? normalizedTarget = NormalizeWorktreePath(request.Path);
        target = normalizedTarget;
        DeveloperGitWorktree? selected = normalizedTarget is null ? null : worktrees.SingleOrDefault(worktree =>
            worktree.Path.Equals(normalizedTarget, StringComparison.Ordinal));
        if (selected is null) return "Select an existing linked worktree.";
        if (selected.IsMain) return "The original workspace cannot be removed as a linked worktree.";
        if (selected.IsHarnessManaged) return "Harness-managed goal worktrees cannot be removed here.";
        if (selected.IsLocked) return "Unlock this worktree with Git before removing it.";
        if (request.ExpectedSelectedWorktreeFingerprint is null ||
            !CryptographicEquals(selected.StateFingerprint.Value,
                request.ExpectedSelectedWorktreeFingerprint.Value))
            return "The selected worktree changed after display. Refresh and retry.";
        if ((selected.IsDirty || selected.HasConflicts) && !request.Force)
            return "The worktree has uncommitted content. Review it and explicitly enable forced removal.";
        return null;
    }

    private DeveloperGitWorktree[] MapWorktrees(
        Repository repository,
        CancellationToken cancellationToken)
    {
        List<DeveloperGitWorktree> mapped =
        [
            MapWorktreeRepository(repository, isMain: true, isLocked: false, lockReason: null,
                cancellationToken),
        ];
        foreach (Worktree worktree in repository.Worktrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Repository linked = worktree.WorktreeRepository;
            mapped.Add(MapWorktreeRepository(linked, isMain: false, worktree.IsLocked,
                Bound(worktree.LockReason, 1024), cancellationToken));
        }
        return mapped.OrderBy(worktree => worktree.Path, StringComparer.Ordinal).ToArray();
    }

    private DeveloperGitWorktree MapWorktreeRepository(
        Repository repository,
        bool isMain,
        bool isLocked,
        string? lockReason,
        CancellationToken cancellationToken)
    {
        WorkspaceGitState state = GitRepositoryStateReader.Read(repository, cancellationToken);
        string path = NormalizeRoot(repository.Info.WorkingDirectory);
        return new(path,
            repository.Info.IsHeadDetached ? null : repository.Head.FriendlyName,
            repository.Head.Tip?.Sha ?? string.Empty,
            isMain,
            isLocked,
            lockReason,
            state.Changes.Count > 0,
            state.Changes.Any(change => change.IsConflicted),
            IsHarnessManaged(path),
            new(state.Fingerprint));
    }

    private bool IsHarnessManaged(string path) => applicationPaths is not null &&
        IsAtOrBelow(path, applicationPaths.Current.WorktreeDirectory);

    private static DeveloperGitWorktreeSetFingerprint WorktreeFingerprint(
        IReadOnlyList<DeveloperGitWorktree> worktrees)
    {
        StringBuilder input = new();
        foreach (DeveloperGitWorktree worktree in worktrees)
            input.Append(worktree.Path).Append('\0').Append(worktree.Branch).Append('\0')
                .Append(worktree.HeadSha).Append('\0').Append(worktree.IsMain).Append('\0')
                .Append(worktree.IsLocked).Append('\0').Append(worktree.LockReason).Append('\0')
                .Append(worktree.IsDirty).Append('\0').Append(worktree.HasConflicts).Append('\0')
                .Append(worktree.IsHarnessManaged).Append('\0')
                .Append(worktree.StateFingerprint.Value).Append('\n');
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())))
            .ToLowerInvariant());
    }

    private static string? NormalizeWorktreePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        try { return NormalizeRoot(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                           PathTooLongException)
        { return null; }
    }

    private static bool IsAtOrBelow(string candidate, string root)
    {
        string normalizedCandidate = NormalizeRoot(candidate);
        string normalizedRoot = NormalizeRoot(root);
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.Ordinal) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal);
    }

    private static string? Bound(string? value, int maximum) => string.IsNullOrWhiteSpace(value)
        ? null : value.Length <= maximum ? value : value[..maximum];

    private static async Task<int> RunWorktreeGitAsync(
        string root,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreateGitStartInfo(root);
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
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

    private static async Task<DeveloperGitStash[]> ReadStashesAsync(
        Repository repository,
        string root,
        CancellationToken cancellationToken)
    {
        if (repository.Refs["refs/stash"] is null) return [];
        ProcessStartInfo startInfo = CreateGitStartInfo(root);
        foreach (string argument in new[]
                 {
                     "log", "-g", "--max-count=500",
                     "--format=%gd%x00%H%x00%P%x00%cI%x00%gs", "-z", "refs/stash",
                 })
            startInfo.ArgumentList.Add(argument);
        ProcessOutput output = await RunGitForOutputAsync(startInfo, cancellationToken);
        if (output.ExitCode != 0 || output.IsTruncated)
            throw new InvalidOperationException("The bounded stash list could not be read.");
        string[] fields = output.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length % 5 != 0)
            throw new InvalidOperationException("The stash list format was invalid.");
        List<DeveloperGitStash> stashes = [];
        for (int index = 0; index < fields.Length; index += 5)
        {
            if (!DateTimeOffset.TryParse(fields[index + 3], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset createdAt))
                throw new InvalidOperationException("The stash timestamp was invalid.");
            string message = fields[index + 4];
            bool truncated = message.Length > 1024;
            stashes.Add(new(fields[index], fields[index + 1],
                fields[index + 2].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty,
                createdAt, truncated ? message[..1024] : message, truncated));
        }
        return stashes.ToArray();
    }

    private static async Task<ProcessOutput> RunGitForOutputAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        const int maximumCharacters = 2 * 1024 * 1024;
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start()) return new(-1, string.Empty, false);
        StringBuilder output = new();
        bool truncated = false;
        Task standardOutput = Task.Run(async () =>
        {
            char[] buffer = new char[4096];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(
                       buffer.AsMemory(), cancellationToken)) > 0)
            {
                int available = maximumCharacters - output.Length;
                if (available > 0) output.Append(buffer, 0, Math.Min(read, available));
                truncated |= read > available;
            }
        }, CancellationToken.None);
        Task standardError = DrainAsync(process.StandardError, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutput, standardError);
            return new(process.ExitCode, output.ToString(), truncated);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<int> RunGitAsync(
        string root,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreateGitStartInfo(root);
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
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

    private static DeveloperGitWorktreeInspection WorktreeInspectionFailure(string code, string error) =>
        new(null, null, [], code, error);

    private static DeveloperGitStashInspection StashInspectionFailure(string code, string error) =>
        new(null, [], code, error);

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
        try
        {
            IOException? inputFailure = await GitStandardInputWriter.WriteAndCloseAsync(
                process.StandardInput, request.Message, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardError, standardOutput);
            if (process.ExitCode == 0 && inputFailure is not null) throw inputFailure;
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

    private sealed record ProcessOutput(int ExitCode, string StandardOutput, bool IsTruncated);

    private sealed class GitPatchUnitUnavailableException : Exception;}
