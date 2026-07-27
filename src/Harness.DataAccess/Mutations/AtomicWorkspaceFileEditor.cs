using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Mutations;

internal sealed partial class AtomicWorkspaceFileEditor : IWorkspaceFileEditor
{
    private const int MaximumContentBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async ValueTask<WorkspaceFileEditResult> ApplyAsync(
        string worktreeRoot,
        WorkspaceFileEdit edit,
        CancellationToken cancellationToken = default)
    {
        if (edit.ExpectedSha256 is not null && !Sha256Pattern().IsMatch(edit.ExpectedSha256))
        {
            return Failure(edit.Path, "invalid_hash", "The expected SHA-256 must be lowercase hexadecimal.");
        }

        int contentBytes = Utf8WithoutBom.GetByteCount(edit.Content);
        if (contentBytes > MaximumContentBytes)
        {
            return Failure(edit.Path, "content_too_large", "Edited content cannot exceed 1 MiB.");
        }

        if (!WorkspacePathPolicy.TryResolve(
                worktreeRoot,
                edit.Path,
                out _,
                out string confinedPath,
                out string targetPath,
                out string? errorCode,
                out string? error))
        {
            return Failure(confinedPath, errorCode!, error!);
        }

        string? parent = Path.GetDirectoryName(targetPath);
        if (parent is null || !Directory.Exists(parent))
        {
            return Failure(confinedPath, "parent_missing", "The destination directory does not exist.");
        }

        FileInfo target = new(targetPath);
        bool wasCreated = !target.Exists;
        if (target.Exists && target.Length > MaximumContentBytes)
        {
            return Failure(confinedPath, "existing_file_too_large", "The existing file exceeds 1 MiB.");
        }

        string? previousHash = target.Exists
            ? await HashAsync(target.FullName, cancellationToken)
            : null;
        if (!string.Equals(previousHash, edit.ExpectedSha256, StringComparison.Ordinal))
        {
            return Failure(
                confinedPath,
                "content_changed",
                "The file no longer matches the expected content hash.",
                previousHash);
        }

        string temporaryPath = Path.Combine(parent, $".harness-edit-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                edit.Content,
                Utf8WithoutBom,
                cancellationToken);
            if (target.Exists && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(target.FullName));
            }

            string? latestHash = File.Exists(target.FullName)
                ? await HashAsync(target.FullName, cancellationToken)
                : null;
            if (!string.Equals(latestHash, edit.ExpectedSha256, StringComparison.Ordinal))
            {
                return Failure(
                    confinedPath,
                    "content_changed",
                    "The file changed while the edit was being prepared.",
                    latestHash);
            }

            File.Move(temporaryPath, target.FullName, overwrite: true);
            string newHash = Convert.ToHexStringLower(
                SHA256.HashData(Utf8WithoutBom.GetBytes(edit.Content)));
            return new(
                confinedPath,
                previousHash,
                newHash,
                contentBytes,
                wasCreated,
                ErrorCode: null,
                Error: null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(confinedPath, "write_failed", exception.Message, previousHash);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static async ValueTask<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static WorkspaceFileEditResult Failure(
        string path,
        string code,
        string error,
        string? previousHash = null) =>
        new(path, previousHash, null, 0, WasCreated: false, code, error);

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
