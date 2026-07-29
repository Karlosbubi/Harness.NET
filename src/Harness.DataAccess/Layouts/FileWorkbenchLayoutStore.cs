using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Layouts;

internal sealed class FileWorkbenchLayoutStore(IApplicationPaths applicationPaths)
    : IWorkbenchLayoutStore
{
    internal const string Format = "harness-workbench-layout-v1";
    internal const int Version = 1;
    internal const int MaximumPayloadBytes = 256 * 1024;
    private const int MaximumEnvelopeBytes = MaximumPayloadBytes + 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public async ValueTask<WorkbenchLayoutStoreReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        string path = applicationPaths.Current.WorkbenchLayoutPath;
        if (!File.Exists(path))
        {
            return new(null, null, null);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file = new(path);
            if (file.Length > MaximumEnvelopeBytes)
            {
                return FailureRead(
                    WorkbenchLayoutStoreFailure.TooLarge,
                    "The saved workbench layout exceeds the supported size.");
            }

            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            LayoutEnvelope? envelope = await JsonSerializer.DeserializeAsync<LayoutEnvelope>(
                stream,
                JsonOptions,
                cancellationToken);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Payload) ||
                string.IsNullOrWhiteSpace(envelope.PayloadSha256))
            {
                return FailureRead(
                    WorkbenchLayoutStoreFailure.InvalidContent,
                    "The saved workbench layout is incomplete.");
            }

            if (!string.Equals(envelope.Format, Format, StringComparison.Ordinal) ||
                envelope.Version != Version)
            {
                return FailureRead(
                    WorkbenchLayoutStoreFailure.UnsupportedVersion,
                    "The saved workbench layout uses an unsupported format version.");
            }

            byte[] payload = Encoding.UTF8.GetBytes(envelope.Payload);
            if (payload.Length > MaximumPayloadBytes)
            {
                return FailureRead(
                    WorkbenchLayoutStoreFailure.TooLarge,
                    "The saved workbench layout payload exceeds the supported size.");
            }

            if (!HashMatches(payload, envelope.PayloadSha256))
            {
                return FailureRead(
                    WorkbenchLayoutStoreFailure.IntegrityMismatch,
                    "The saved workbench layout failed its integrity check.");
            }

            return new(new(envelope.Payload), null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          JsonException or NotSupportedException)
        {
            return FailureRead(WorkbenchLayoutStoreFailure.StorageUnavailable, exception.Message);
        }
    }

    public async ValueTask<WorkbenchLayoutStoreWriteResult> WriteAsync(
        WorkbenchLayoutContent layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (string.IsNullOrWhiteSpace(layout.Value))
        {
            return FailureWrite(
                WorkbenchLayoutStoreFailure.InvalidContent,
                "A non-empty workbench layout is required.");
        }

        byte[] payload = Encoding.UTF8.GetBytes(layout.Value);
        if (payload.Length > MaximumPayloadBytes)
        {
            return FailureWrite(
                WorkbenchLayoutStoreFailure.TooLarge,
                "The workbench layout payload exceeds the supported size.");
        }

        string path = applicationPaths.Current.WorkbenchLayoutPath;
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(
            directory,
            $".workbench-layout-{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            LayoutEnvelope envelope = new(
                Format,
                Version,
                layout.Value,
                Convert.ToHexStringLower(SHA256.HashData(payload)));
            await using (FileStream stream = new(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    envelope,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    temporary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporary, path, overwrite: true);
            return new(true, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          JsonException or NotSupportedException)
        {
            return FailureWrite(WorkbenchLayoutStoreFailure.StorageUnavailable, exception.Message);
        }
        finally
        {
            DeleteTemporary(temporary);
        }
    }

    public ValueTask<WorkbenchLayoutStoreWriteResult> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(applicationPaths.Current.WorkbenchLayoutPath);
            return ValueTask.FromResult(new WorkbenchLayoutStoreWriteResult(true, null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult(FailureWrite(
                WorkbenchLayoutStoreFailure.StorageUnavailable,
                exception.Message));
        }
    }

    private static bool HashMatches(byte[] payload, string expected)
    {
        try
        {
            byte[] expectedBytes = Convert.FromHexString(expected);
            byte[] actualBytes = SHA256.HashData(payload);
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void DeleteTemporary(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static WorkbenchLayoutStoreReadResult FailureRead(
        WorkbenchLayoutStoreFailure failure,
        string error) => new(null, failure, error);

    private static WorkbenchLayoutStoreWriteResult FailureWrite(
        WorkbenchLayoutStoreFailure failure,
        string error) => new(false, failure, error);

    private sealed record LayoutEnvelope(
        string Format,
        int Version,
        string Payload,
        string PayloadSha256);
}
