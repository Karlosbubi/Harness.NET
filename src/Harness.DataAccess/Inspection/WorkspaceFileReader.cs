using System.Text;

namespace Harness.DataAccess.Inspection;

internal sealed class WorkspaceFileReader : IWorkspaceFileReader
{
    private const int MaximumContentBytes = 64 * 1024;
    private static readonly char[] Separators =
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async ValueTask<WorkspaceFileRead> ReadAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return Failure(relativePath, "invalid_path", "A workspace root and relative path are required.");
        }

        string canonicalRoot;
        string targetPath;
        try
        {
            canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            targetPath = Path.GetFullPath(relativePath, canonicalRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Failure(relativePath, "invalid_path", exception.Message);
        }

        if (!Directory.Exists(canonicalRoot))
        {
            return Failure(relativePath, "workspace_missing", "The workspace root does not exist.");
        }

        string confinedPath = Path.GetRelativePath(canonicalRoot, targetPath);
        if (Path.IsPathRooted(relativePath) || IsOutsideWorkspace(confinedPath))
        {
            return Failure(relativePath, "outside_workspace", "The path must remain inside the workspace.");
        }

        if (ContainsReparsePoint(canonicalRoot, confinedPath))
        {
            return Failure(confinedPath, "symlink_not_allowed", "Symbolic links are not allowed in inspected paths.");
        }

        FileInfo file = new(targetPath);
        if (!file.Exists)
        {
            return Failure(confinedPath, "file_missing", "The requested file does not exist.");
        }

        try
        {
            int bytesToRead = (int)Math.Min(file.Length, MaximumContentBytes);
            byte[] buffer = new byte[bytesToRead];
            await using FileStream stream = new(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            bool isTruncated = file.Length > MaximumContentBytes;
            string content = DecodeUtf8(buffer, offset, isTruncated);
            return new(
                confinedPath,
                content,
                file.Length,
                isTruncated,
                ErrorCode: null,
                Error: null);
        }
        catch (DecoderFallbackException)
        {
            return Failure(confinedPath, "not_text", "The requested file is not valid UTF-8 text.", file.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(confinedPath, "read_failed", exception.Message, file.Length);
        }
    }

    private static bool IsOutsideWorkspace(string relativePath) =>
        relativePath.Equals("..", StringComparison.Ordinal) ||
        relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
        relativePath.Equals(".", StringComparison.Ordinal);

    private static bool ContainsReparsePoint(string root, string relativePath)
    {
        string current = root;
        foreach (string segment in relativePath.Split(
                     Separators,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string DecodeUtf8(byte[] buffer, int length, bool isTruncated)
    {
        Decoder decoder = StrictUtf8.GetDecoder();
        char[] characters = new char[StrictUtf8.GetMaxCharCount(length)];
        decoder.Convert(
            buffer,
            0,
            length,
            characters,
            0,
            characters.Length,
            flush: !isTruncated,
            out _,
            out int charactersUsed,
            out _);
        return new string(characters, 0, charactersUsed);
    }

    private static WorkspaceFileRead Failure(
        string path,
        string code,
        string error,
        long sizeBytes = 0) =>
        new(path, string.Empty, sizeBytes, IsTruncated: false, code, error);
}
