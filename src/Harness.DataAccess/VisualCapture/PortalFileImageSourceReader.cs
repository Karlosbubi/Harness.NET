namespace Harness.DataAccess.VisualCapture;

internal sealed class PortalFileImageSourceReader : IVisualCaptureImageSourceReader
{
    public async ValueTask<PortalImageReadResult> ReadAsync(
        Uri uri,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (uri is null || !uri.IsAbsoluteUri || !uri.IsFile ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return Failure(PortalImageReadState.InvalidUri, "capture_uri_invalid",
                "The portal returned a non-file screenshot URI.");
        }
        if (maximumBytes is < 1 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        try
        {
            string path = Path.GetFullPath(uri.LocalPath);
            FileInfo info = new(path);
            if (!info.Exists)
            {
                return Failure(PortalImageReadState.Missing, "capture_file_missing",
                    "The portal screenshot is no longer available.");
            }
            if (info.Length is <= 0 || info.Length > maximumBytes)
            {
                return Failure(PortalImageReadState.TooLarge, "capture_size_rejected",
                    $"The encoded screenshot exceeds the {maximumBytes}-byte limit.");
            }

            byte[] content = new byte[info.Length];
            await using FileStream stream = new(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream.ReadExactlyAsync(content, cancellationToken);
            return new(PortalImageReadState.Succeeded, content, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or NotSupportedException)
        {
            return Failure(PortalImageReadState.Failed, "capture_read_failed", exception.Message);
        }
    }

    private static PortalImageReadResult Failure(
        PortalImageReadState state,
        string code,
        string error) => new(state, ReadOnlyMemory<byte>.Empty, code, error);
}
