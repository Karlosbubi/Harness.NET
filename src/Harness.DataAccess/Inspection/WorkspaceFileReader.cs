using System.Security.Cryptography;
using System.Text;

namespace Harness.DataAccess.Inspection;

internal sealed class WorkspaceFileReader : IWorkspaceFileReader
{
    private const int MaximumContentBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async ValueTask<WorkspaceFileRead> ReadAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (!WorkspacePathPolicy.TryResolve(
                workspaceRoot,
                relativePath,
                out _,
                out string confinedPath,
                out string targetPath,
                out string? errorCode,
                out string? error))
        {
            return Failure(confinedPath, errorCode!, error!);
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
            string? sha256 = isTruncated
                ? null
                : Convert.ToHexStringLower(SHA256.HashData(buffer.AsSpan(0, offset)));
            return new(
                confinedPath,
                content,
                sha256,
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
        new(path, string.Empty, Sha256: null, sizeBytes, IsTruncated: false, code, error);
}
