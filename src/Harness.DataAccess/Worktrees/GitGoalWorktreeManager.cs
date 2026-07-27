using System.Diagnostics;
using System.Text.RegularExpressions;
using Harness.DataAccess.Configuration;
using LibGit2Sharp;

namespace Harness.DataAccess.Worktrees;

internal sealed partial class GitGoalWorktreeManager(IApplicationPaths applicationPaths)
    : IGoalWorktreeManager
{
    private const int MaximumDiagnosticCharacters = 16 * 1024;

    public async ValueTask<GoalWorktreeResult> CreateAsync(
        string goalId,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        if (!GoalIdPattern().IsMatch(goalId))
        {
            return Failure(goalId, "invalid_goal", "The goal identifier must contain 32 lowercase hexadecimal characters.");
        }

        string? discovered = Repository.Discover(repositoryRoot);
        if (discovered is null)
        {
            return Failure(goalId, "repository_missing", "No Git repository was found.");
        }

        string root;
        string baseCommit;
        try
        {
            using Repository repository = new(discovered);
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository.Info.WorkingDirectory));
            if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot))
                    .Equals(root, StringComparison.Ordinal))
            {
                return Failure(goalId, "repository_mismatch", "The workspace must be the Git repository root.");
            }

            if (repository.Head.Tip is null)
            {
                return Failure(goalId, "repository_unborn", "The repository must have an initial commit.");
            }

            baseCommit = repository.Head.Tip.Sha;
        }
        catch (LibGit2SharpException exception)
        {
            return Failure(goalId, "repository_failed", exception.Message);
        }

        string branch = $"harness/goal-{goalId[..12]}";
        string worktreePath = Path.Combine(applicationPaths.Current.WorktreeDirectory, goalId);
        if (Directory.Exists(worktreePath))
        {
            GoalWorktreeResult? existing = InspectExisting(goalId, branch, worktreePath, baseCommit);
            return existing ?? Failure(
                goalId,
                "worktree_conflict",
                "The goal worktree path exists but does not match the requested goal.",
                branch,
                worktreePath,
                baseCommit);
        }

        Directory.CreateDirectory(applicationPaths.Current.WorktreeDirectory);
        ProcessResult process = await RunGitAsync(
            root,
            ["worktree", "add", "-b", branch, "--no-track", worktreePath, baseCommit],
            cancellationToken);
        if (process.ExitCode != 0)
        {
            return Failure(
                goalId,
                "worktree_create_failed",
                string.IsNullOrWhiteSpace(process.Error) ? process.Output : process.Error,
                branch,
                worktreePath,
                baseCommit);
        }

        return new(goalId, branch, worktreePath, baseCommit, WasCreated: true, null, null);
    }

    private static GoalWorktreeResult? InspectExisting(
        string goalId,
        string branch,
        string worktreePath,
        string baseCommit)
    {
        try
        {
            using Repository repository = new(worktreePath);
            return repository.Head.FriendlyName.Equals(branch, StringComparison.Ordinal) &&
                   repository.Head.Tip?.Sha == baseCommit
                ? new(goalId, branch, worktreePath, baseCommit, WasCreated: false, null, null)
                : null;
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
    }

    private static async ValueTask<ProcessResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new(-1, string.Empty, "Git did not start.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(-1, string.Empty, exception.Message);
        }

        Task<string> output = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        Task<string> error = ReadBoundedAsync(process.StandardError, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return new(process.ExitCode, await output, await error);
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        System.Text.StringBuilder kept = new(MaximumDiagnosticCharacters);
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            int remaining = MaximumDiagnosticCharacters - kept.Length;
            if (remaining > 0)
            {
                kept.Append(buffer, 0, Math.Min(read, remaining));
            }
        }

        return kept.ToString().Trim();
    }

    private static GoalWorktreeResult Failure(
        string goalId,
        string code,
        string error,
        string branch = "",
        string path = "",
        string baseCommit = "") =>
        new(goalId, branch, path, baseCommit, WasCreated: false, code, error);

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GoalIdPattern();

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);
}
