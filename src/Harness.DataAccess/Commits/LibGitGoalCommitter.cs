using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;

namespace Harness.DataAccess.Commits;

internal sealed class LibGitGoalCommitter : IGoalCommitter
{
    private const int MaximumDiffBytes = 1024 * 1024;

    public ValueTask<GoalCommitInspection> InspectAsync(
        GoalCommitInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidationResult validation = ValidateInspectionRequest(request);
        if (validation.Error is not null)
        {
            return ValueTask.FromResult(InspectionFailure(validation.ErrorCode!, validation.Error));
        }

        try
        {
            using Repository repository = new(request.WorktreePath.Value);
            string? mismatch = ValidateRepository(repository, request.WorktreePath,
                request.ExpectedBranch);
            if (mismatch is not null)
            {
                return ValueTask.FromResult(InspectionFailure("worktree_mismatch", mismatch));
            }

            return ValueTask.FromResult(Inspect(repository));
        }
        catch (Exception exception) when (exception is LibGit2SharpException or ArgumentException)
        {
            return ValueTask.FromResult(InspectionFailure("repository_failed", exception.Message));
        }
    }

    public ValueTask<GoalCommitResult> CommitAsync(
        GoalCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? requestError = ValidateCommitRequest(request);
        if (requestError is not null)
        {
            return ValueTask.FromResult(Failure("invalid_commit_request", requestError));
        }

        try
        {
            using Repository repository = new(request.WorktreePath.Value);
            string? mismatch = ValidateRepository(repository, request.WorktreePath,
                request.ExpectedBranch);
            if (mismatch is not null)
            {
                return ValueTask.FromResult(Failure("worktree_mismatch", mismatch));
            }

            Commit? head = repository.Head.Tip;
            if (head is not null && IsApprovedCommit(head, request))
            {
                return ValueTask.FromResult(new GoalCommitResult(
                    new(head.Sha), WasReconciled: true, ErrorCode: null, Error: null));
            }

            if (head?.Sha != request.ExpectedHead.Value)
            {
                return ValueTask.FromResult(Failure(
                    "head_changed", "The goal branch HEAD changed after commit approval."));
            }

            GoalCommitInspection inspection = Inspect(repository);
            if (inspection.Error is not null)
            {
                return ValueTask.FromResult(Failure(inspection.ErrorCode!, inspection.Error));
            }

            if (inspection.DiffSha256 != request.ExpectedDiffSha256)
            {
                return ValueTask.FromResult(Failure(
                    "diff_changed", "The goal worktree diff changed after commit approval."));
            }

            Commands.Stage(repository, "*");
            cancellationToken.ThrowIfCancellationRequested();
            Signature signature = new(
                request.AuthorName.Value,
                request.AuthorEmail.Value,
                request.CreatedAt);
            Commit commit = repository.Commit(request.Message.Value, signature, signature);
            return ValueTask.FromResult(new GoalCommitResult(
                new(commit.Sha), WasReconciled: false, ErrorCode: null, Error: null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is LibGit2SharpException or ArgumentException)
        {
            return ValueTask.FromResult(Failure("commit_failed", exception.Message));
        }
    }

    private static GoalCommitInspection Inspect(Repository repository)
    {
        if (repository.Head.Tip is null)
        {
            return InspectionFailure("repository_unborn", "The goal branch has no HEAD commit.");
        }

        StatusEntry[] status = repository.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
        }).Where(item => item.State is not FileStatus.Ignored)
          .OrderBy(item => item.FilePath, StringComparer.Ordinal)
          .ToArray();
        if (status.Length == 0)
        {
            return InspectionFailure("no_changes", "The goal worktree has no changes to commit.");
        }

        if (status.Any(item => item.State.HasFlag(FileStatus.Conflicted)))
        {
            return InspectionFailure(
                "conflicts_present", "Resolve all Git conflicts before commit approval.");
        }

        using Patch patch = repository.Diff.Compare<Patch>(
            repository.Head.Tip.Tree,
            DiffTargets.Index | DiffTargets.WorkingDirectory);
        StringBuilder complete = new(patch.Content);
        complete.AppendLine().AppendLine("HARNESS FILE MANIFEST");
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repository.Info.WorkingDirectory));
        long changedContentBytes = 0;
        foreach (StatusEntry entry in status)
        {
            string path = Path.GetFullPath(entry.FilePath, root);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return InspectionFailure(
                    "path_outside_worktree", "A changed path escapes the goal worktree.");
            }

            if (!File.Exists(path))
            {
                complete.Append(entry.State).Append(" | ").Append(entry.FilePath)
                    .AppendLine(" | deleted");
                continue;
            }

            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                return InspectionFailure(
                    "symbolic_link_unsupported",
                    $"Changed symbolic link '{entry.FilePath}' cannot be committed automatically.");
            }

            long fileLength = new FileInfo(path).Length;
            changedContentBytes = checked(changedContentBytes + fileLength);
            if (fileLength > MaximumDiffBytes || changedContentBytes > MaximumDiffBytes)
            {
                return InspectionFailure(
                    "diff_too_large",
                    $"Changed file content exceeds the {MaximumDiffBytes}-byte approval limit.");
            }

            byte[] content = File.ReadAllBytes(path);
            complete.Append(entry.State).Append(" | ").Append(entry.FilePath)
                .Append(" | ").Append(content.Length).Append(" bytes | sha256 ")
                .AppendLine(Convert.ToHexStringLower(SHA256.HashData(content)));
            if (entry.State.HasFlag(FileStatus.NewInWorkdir))
            {
                complete.AppendLine("--- untracked content ---");
                complete.AppendLine(ToDisplayText(content));
                complete.AppendLine("--- end untracked content ---");
            }
        }

        string completeDiff = complete.ToString();
        int diffBytes = Encoding.UTF8.GetByteCount(completeDiff);
        if (diffBytes > MaximumDiffBytes)
        {
            return InspectionFailure(
                "diff_too_large",
                $"The complete diff exceeds the {MaximumDiffBytes}-byte approval limit.");
        }

        return new(
            new(repository.Head.FriendlyName),
            new(repository.Head.Tip.Sha),
            new(Hash(completeDiff)),
            new(completeDiff),
            new(status.Length),
            ErrorCode: null,
            Error: null);
    }

    private static string ToDisplayText(byte[] content)
    {
        if (content.Contains((byte)0))
        {
            return "[binary content; approve using the manifest size and SHA-256]";
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return "[binary content; approve using the manifest size and SHA-256]";
        }
    }

    private static bool IsApprovedCommit(Commit head, GoalCommitRequest request)
    {
        Commit? parent = head.Parents.SingleOrDefault();
        return parent?.Sha == request.ExpectedHead.Value &&
               head.Message.TrimEnd().Equals(request.Message.Value.TrimEnd(),
                   StringComparison.Ordinal) &&
               head.Author.Name.Equals(request.AuthorName.Value, StringComparison.Ordinal) &&
               head.Author.Email.Equals(request.AuthorEmail.Value, StringComparison.Ordinal) &&
               head.Author.When == request.CreatedAt;
    }

    private static string? ValidateRepository(
        Repository repository,
        GoalWorktreePath requestedPath,
        GitBranchName expectedBranch)
    {
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repository.Info.WorkingDirectory));
        string requested = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(requestedPath.Value));
        if (!root.Equals(requested, StringComparison.Ordinal))
        {
            return "The requested path is not the repository worktree root.";
        }

        return repository.Head.FriendlyName.Equals(expectedBranch.Value, StringComparison.Ordinal)
            ? null
            : "The worktree branch does not match the approved goal branch.";
    }

    private static ValidationResult ValidateInspectionRequest(GoalCommitInspectionRequest request)
    {
        if (request?.WorktreePath is null || request.ExpectedBranch is null ||
            string.IsNullOrWhiteSpace(request.WorktreePath.Value) ||
            !Path.IsPathFullyQualified(request.WorktreePath.Value) ||
            string.IsNullOrWhiteSpace(request.ExpectedBranch.Value))
        {
            return new("invalid_inspection_request",
                "A full worktree path and expected branch are required.");
        }

        return new(null, null);
    }

    private static string? ValidateCommitRequest(GoalCommitRequest request)
    {
        ValidationResult inspection = ValidateInspectionRequest(new(
            request.WorktreePath, request.ExpectedBranch));
        if (inspection.Error is not null || request.ExpectedHead is null ||
            request.ExpectedDiffSha256 is null || request.Message is null ||
            request.AuthorName is null || request.AuthorEmail is null ||
            !IsSha(request.ExpectedHead.Value, 40) ||
            !IsSha(request.ExpectedDiffSha256.Value, 64) ||
            string.IsNullOrWhiteSpace(request.Message.Value) ||
            request.Message.Value.Length > 4096 ||
            !request.Message.Value.Contains(
                $"Harness-Diff-SHA256: {request.ExpectedDiffSha256.Value}",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.AuthorName.Value) ||
            request.AuthorName.Value.Length > 256 ||
            string.IsNullOrWhiteSpace(request.AuthorEmail.Value) ||
            request.AuthorEmail.Value.Length > 320 ||
            !request.AuthorEmail.Value.Contains('@', StringComparison.Ordinal))
        {
            return inspection.Error ?? "The approved commit metadata is invalid.";
        }

        return null;
    }

    private static bool IsSha(string value, int length) =>
        value.Length == length && value.All(Uri.IsHexDigit);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static GoalCommitInspection InspectionFailure(string code, string error) =>
        new(null, null, null, new(string.Empty), new(0), code, error);

    private static GoalCommitResult Failure(string code, string error) =>
        new(null, WasReconciled: false, code, error);

    private sealed record ValidationResult(string? ErrorCode, string? Error);
}
