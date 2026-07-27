using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.SemanticIndex;

internal sealed partial class GitTrackedTextCatalogReader : ITrackedTextCatalogReader
{
    private const int MaximumTrackedFiles = 10_000;
    private const int MaximumFileBytes = 1024 * 1024;
    private const long MaximumCatalogBytes = 32L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly HashSet<string> EligibleExtensions = new(
        [
            ".cs", ".fs", ".vb", ".csx", ".fsx",
            ".razor", ".cshtml", ".xaml", ".axaml", ".resx", ".sql",
            ".csproj", ".fsproj", ".vbproj", ".props", ".targets",
            ".sln", ".slnx", ".md", ".txt", ".json", ".jsonc",
            ".xml", ".config", ".yaml", ".yml", ".toml", ".editorconfig",
            ".sh", ".ps1", ".cmd",
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> EligibleExtensionlessFiles = new(
        [".gitignore", ".gitattributes", ".dockerignore", "Dockerfile"],
        StringComparer.OrdinalIgnoreCase);

    public async ValueTask<TrackedTextCatalog> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        string? repositoryPath = Repository.Discover(workspaceRoot);
        if (repositoryPath is null)
        {
            return Failure("repository_missing", "No Git repository was found.");
        }

        try
        {
            using Repository repository = new(repositoryPath);
            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(repository.Info.WorkingDirectory));
            string requestedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            if (!requestedRoot.Equals(root, StringComparison.Ordinal))
            {
                return Failure(
                    "repository_mismatch",
                    "The workspace root must be the Git repository root.");
            }

            string[] trackedPaths = repository.Index
                .Select(entry => entry.Path)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            List<TrackedTextDocument> documents = [];
            int skipped = 0;
            long catalogBytes = 0;
            bool truncated = trackedPaths.Length > MaximumTrackedFiles;
            foreach (string relativePath in trackedPaths.Take(MaximumTrackedFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsEligiblePath(relativePath) ||
                    !WorkspacePathPolicy.TryResolve(
                        root,
                        relativePath,
                        out _,
                        out string confinedPath,
                        out string targetPath,
                        out _,
                        out _))
                {
                    skipped++;
                    continue;
                }

                FileInfo file = new(targetPath);
                if (!file.Exists || file.Length is 0 or > MaximumFileBytes ||
                    catalogBytes + file.Length > MaximumCatalogBytes)
                {
                    skipped++;
                    truncated |= catalogBytes + file.Length > MaximumCatalogBytes;
                    continue;
                }

                string? content = await ReadEligibleTextAsync(file.FullName, cancellationToken);
                if (content is null || LooksSensitive(confinedPath, content))
                {
                    skipped++;
                    continue;
                }

                catalogBytes += file.Length;
                documents.Add(new(confinedPath, content, Hash(content)));
            }

            return new(
                documents,
                trackedPaths.Length,
                skipped + Math.Max(0, trackedPaths.Length - MaximumTrackedFiles),
                truncated,
                ErrorCode: null,
                Error: null);
        }
        catch (LibGit2SharpException exception)
        {
            return Failure("repository_failed", exception.Message);
        }
    }

    private static bool IsEligiblePath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string fileName = Path.GetFileName(normalized);
        if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase) ||
            SensitiveFileNameRegex().IsMatch(fileName) ||
            GeneratedFileNameRegex().IsMatch(fileName))
        {
            return false;
        }

        return EligibleExtensionlessFiles.Contains(fileName) ||
            EligibleExtensions.Contains(Path.GetExtension(fileName));
    }

    private static async ValueTask<string?> ReadEligibleTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (bytes.AsSpan().Contains((byte)0))
            {
                return null;
            }

            return StrictUtf8.GetString(bytes);
        }
        catch (Exception exception) when (exception is
            DecoderFallbackException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool LooksSensitive(string path, string content) =>
        path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal) ||
        content.Contains("-----BEGIN RSA PRIVATE KEY-----", StringComparison.Ordinal) ||
        AssignedSecretRegex().IsMatch(content);

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static TrackedTextCatalog Failure(string code, string error) =>
        new([], 0, 0, IsTruncated: false, code, error);

    [GeneratedRegex(
        "(^|[._-])(secret|secrets|credential|credentials|private[-_]?key|id_rsa)([._-]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveFileNameRegex();

    [GeneratedRegex(
        "(\\.g\\.(cs|fs|vb)|\\.generated\\.(cs|fs|vb)|\\.designer\\.(cs|vb)|\\.assembly(info|attributes)\\.cs)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedFileNameRegex();

    [GeneratedRegex(
        "(?im)(^|[,{])\\s*[\"']?(api[_-]?key|access[_-]?token|client[_-]?secret|password)[\"']?\\s*[:=]\\s*[\"']?(?!<|\\$\\{|%|example|sample|placeholder|changeme)[A-Za-z0-9_./+\\-=]{12,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex AssignedSecretRegex();
}
