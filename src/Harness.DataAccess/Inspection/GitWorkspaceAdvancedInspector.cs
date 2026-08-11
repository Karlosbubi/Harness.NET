using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace Harness.DataAccess.Inspection;

internal sealed class GitWorkspaceAdvancedInspector : IWorkspaceAdvancedInspector
{
    private const int MaximumTrackedFiles = 20_000;
    private const int MaximumFileBytes = 1024 * 1024;
    private const int MaximumLineCharacters = 2_000;
    private const int MaximumPatternCharacters = 512;
    private const int MaximumGlobCharacters = 256;
    private const int MaximumPageSize = 500;
    private const int MaximumRangeLines = 2_000;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public ValueTask<WorkspaceTreeResult> ListTreeAsync(
        string workspaceRoot,
        WorkspaceTreeQuery query,
        CancellationToken cancellationToken = default) =>
        new(Task.Run(() => ListTree(workspaceRoot, query, cancellationToken), cancellationToken));

    public async ValueTask<WorkspaceRangeResult> ReadRangeAsync(
        string workspaceRoot,
        WorkspaceRangeQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.StartLine < 1 || query.LineCount is < 1 or > MaximumRangeLines)
        {
            return RangeFailure(query.Path, "invalid_range",
                $"StartLine must be positive and LineCount must be 1-{MaximumRangeLines}.");
        }

        if (!TryRepository(workspaceRoot, out string root, out string? code, out string? error) ||
            !WorkspacePathPolicy.TryResolve(root, query.Path.Value, out _, out string confined,
                out string target, out code, out error))
        {
            return RangeFailure(query.Path, code ?? "invalid_path", error ?? "Invalid path.");
        }

        FileInfo file = new(target);
        if (!file.Exists)
        {
            return RangeFailure(new(confined), "file_missing", "The tracked file does not exist.");
        }
        if (file.Length > MaximumFileBytes)
        {
            return RangeFailure(new(confined), "file_too_large",
                $"Ranged reads are limited to {MaximumFileBytes} bytes per file.");
        }
        if (!TrackedFiles(root).Contains(confined, StringComparer.Ordinal))
        {
            return RangeFailure(new(confined), "file_untracked",
                "Ranged reads are limited to Git-tracked files.");
        }

        try
        {
            string content = await File.ReadAllTextAsync(target, StrictUtf8, cancellationToken);
            string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            int startIndex = Math.Min(query.StartLine - 1, lines.Length);
            int count = Math.Min(query.LineCount, lines.Length - startIndex);
            string selected = string.Join('\n', lines.Skip(startIndex).Take(count));
            return new(
                new(confined),
                query.StartLine,
                count == 0 ? query.StartLine - 1 : query.StartLine + count - 1,
                lines.Length,
                selected,
                Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(content))),
                startIndex + count < lines.Length,
                null,
                null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            DecoderFallbackException)
        {
            return RangeFailure(new(confined), "file_read_failed", exception.Message);
        }
    }

    public async ValueTask<WorkspaceRegexResult> SearchRegexAsync(
        string workspaceRoot,
        WorkspaceRegexQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.Pattern.Value) ||
            query.Pattern.Value.Length > MaximumPatternCharacters ||
            query.MaximumResults is < 1 or > MaximumPageSize ||
            query.FileGlob?.Value.Length > MaximumGlobCharacters)
        {
            return RegexFailure("invalid_regex_query",
                "Pattern, file glob, or result limit is outside the bounded range.");
        }
        if (!TryOffset(query.Continuation, out int offset))
        {
            return RegexFailure("invalid_continuation", "The continuation is invalid.");
        }
        if (!TryRepository(workspaceRoot, out string root, out string? code, out string? error))
        {
            return RegexFailure(code!, error!);
        }

        Regex regex;
        try
        {
            regex = new(query.Pattern.Value,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException exception)
        {
            return RegexFailure("invalid_regex", exception.Message);
        }

        List<WorkspaceRegexMatch> all = [];
        int filesScanned = 0;
        try
        {
            foreach (string path in TrackedFiles(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (query.FileGlob is not null &&
                    !FileSystemName.MatchesSimpleExpression(
                        query.FileGlob.Value, path, ignoreCase: true))
                {
                    continue;
                }
                if (!WorkspacePathPolicy.TryResolve(root, path, out _, out string confined,
                        out string target, out _, out _) ||
                    new FileInfo(target) is not { Exists: true, Length: <= MaximumFileBytes })
                {
                    continue;
                }

                filesScanned++;
                try
                {
                    string[] lines = (await File.ReadAllTextAsync(
                        target, StrictUtf8, cancellationToken))
                        .Replace("\r\n", "\n", StringComparison.Ordinal)
                        .Split('\n');
                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                    {
                        foreach (Match match in regex.Matches(lines[lineIndex]).Cast<Match>())
                        {
                            all.Add(new(
                                new(confined),
                                lineIndex + 1,
                                match.Index + 1,
                                match.Length,
                                Bound(lines[lineIndex], MaximumLineCharacters)));
                            if (all.Count >= offset + query.MaximumResults + 1)
                            {
                                return Page(all, filesScanned, offset, query.MaximumResults);
                            }
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or
                    UnauthorizedAccessException or DecoderFallbackException or RegexMatchTimeoutException)
                {
                    if (exception is RegexMatchTimeoutException)
                    {
                        return RegexFailure("regex_timeout", "The regular expression exceeded its time limit.");
                    }
                }
            }
            return Page(all, filesScanned, offset, query.MaximumResults);
        }
        catch (LibGit2SharpException exception)
        {
            return RegexFailure("repository_failed", exception.Message);
        }
    }

    private static WorkspaceTreeResult ListTree(
        string workspaceRoot,
        WorkspaceTreeQuery query,
        CancellationToken cancellationToken)
    {
        if (query.MaximumDepth is < 0 or > 32 || query.MaximumResults is < 1 or > MaximumPageSize ||
            query.Glob?.Value.Length > MaximumGlobCharacters ||
            !TryOffset(query.Continuation, out int offset))
        {
            return TreeFailure("invalid_tree_query", "Tree depth, glob, result limit, or continuation is invalid.");
        }
        if (!TryRepository(workspaceRoot, out string root, out string? code, out string? error))
        {
            return TreeFailure(code!, error!);
        }

        string prefix = query.Root.Value.Replace('\\', '/').Trim('/');
        HashSet<string> directories = new(StringComparer.Ordinal);
        List<WorkspaceTreeEntry> entries = [];
        foreach (string file in TrackedFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (prefix.Length > 0 && file != prefix &&
                !file.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                continue;
            }

            string relative = prefix.Length == 0 ? file : file[(prefix.Length + 1)..];
            int depth = relative.Count(character => character == '/');
            string[] parts = file.Split('/');
            int prefixParts = prefix.Length == 0 ? 0 : prefix.Split('/').Length;
            for (int index = prefixParts; index < parts.Length - 1; index++)
            {
                string directory = string.Join('/', parts.Take(index + 1));
                int directoryDepth = index - prefixParts;
                if (directoryDepth <= query.MaximumDepth && directories.Add(directory) &&
                    Matches(query.Glob, directory))
                {
                    entries.Add(new(new(directory), WorkspaceTreeEntryKind.Directory, directoryDepth));
                }
            }
            if (depth <= query.MaximumDepth && Matches(query.Glob, file))
            {
                entries.Add(new(new(file), WorkspaceTreeEntryKind.File, depth));
            }
        }

        WorkspaceTreeEntry[] ordered = entries
            .OrderBy(item => item.Path.Value, StringComparer.Ordinal)
            .ThenBy(item => item.Kind)
            .ToArray();
        WorkspaceTreeEntry[] page = ordered.Skip(offset).Take(query.MaximumResults).ToArray();
        bool truncated = offset + page.Length < ordered.Length;
        return new(page, truncated ? new((offset + page.Length).ToString()) : null,
            truncated, null, null);
    }

    private static IEnumerable<string> TrackedFiles(string root)
    {
        using Repository repository = new(root);
        return repository.Index
            .Select(entry => entry.Path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(MaximumTrackedFiles)
            .ToArray();
    }

    private static bool TryRepository(
        string workspaceRoot,
        out string root,
        out string? code,
        out string? error)
    {
        root = string.Empty;
        code = null;
        error = null;
        string? repositoryPath = Repository.Discover(workspaceRoot);
        if (repositoryPath is null)
        {
            code = "repository_missing";
            error = "No Git repository was found.";
            return false;
        }
        try
        {
            using Repository repository = new(repositoryPath);
            root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(repository.Info.WorkingDirectory));
            if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot))
                    .Equals(root, StringComparison.Ordinal))
            {
                code = "repository_mismatch";
                error = "The workspace root must be the Git repository root.";
                return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or
            UnauthorizedAccessException)
        {
            code = "repository_failed";
            error = exception.Message;
            return false;
        }
    }

    private static WorkspaceRegexResult Page(
        IReadOnlyList<WorkspaceRegexMatch> matches,
        int filesScanned,
        int offset,
        int maximumResults)
    {
        WorkspaceRegexMatch[] page = matches.Skip(offset).Take(maximumResults).ToArray();
        bool truncated = matches.Count > offset + page.Length;
        return new(page, filesScanned,
            truncated ? new((offset + page.Length).ToString()) : null,
            truncated, null, null);
    }

    private static bool TryOffset(WorkspaceInspectionContinuation? continuation, out int offset)
    {
        if (continuation is null)
        {
            offset = 0;
            return true;
        }

        return int.TryParse(continuation.Value, out offset) && offset >= 0;
    }

    private static bool Matches(WorkspaceInspectionPattern? glob, string path) =>
        glob is null || FileSystemName.MatchesSimpleExpression(glob.Value, path, ignoreCase: true);

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static WorkspaceTreeResult TreeFailure(string code, string error) =>
        new([], null, false, code, error);

    private static WorkspaceRangeResult RangeFailure(
        WorkspaceInspectionPath path,
        string code,
        string error) =>
        new(path, 0, 0, 0, string.Empty, null, false, code, error);

    private static WorkspaceRegexResult RegexFailure(string code, string error) =>
        new([], 0, null, false, code, error);
}
