using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Harness.DataAccess.Debugging;

internal sealed class DapProtocolStream(Stream input, Stream output)
{
    internal const int MaximumHeaderBytes = 8 * 1024;
    internal const int MaximumMessageBytes = 4 * 1024 * 1024;
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();
    private readonly SemaphoreSlim writeGate = new(1, 1);

    internal async ValueTask<JsonDocument?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] header = ArrayPool<byte>.Shared.Rent(MaximumHeaderBytes);
        try
        {
            int headerLength = await ReadHeaderAsync(header, cancellationToken);
            if (headerLength == 0) return null;
            int contentLength = ParseContentLength(header.AsSpan(0, headerLength));
            byte[] body = new byte[contentLength];
            await ReadExactlyAsync(body, cancellationToken);
            try
            {
                JsonDocument document = JsonDocument.Parse(body,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 64,
                    });
                if (document.RootElement.ValueKind is not JsonValueKind.Object)
                {
                    document.Dispose();
                    throw new DapProtocolException("A DAP message must be a JSON object.");
                }
                return document;
            }
            catch (JsonException exception)
            {
                throw new DapProtocolException(
                    $"The debug adapter returned invalid JSON: {exception.Message}");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header, clearArray: true);
        }
    }

    internal async ValueTask WriteAsync<T>(
        T message,
        CancellationToken cancellationToken = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message);
        if (body.Length is <= 0 or > MaximumMessageBytes)
            throw new DapProtocolException("The outbound DAP message exceeds its size limit.");
        byte[] header = Encoding.ASCII.GetBytes(
            $"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await output.WriteAsync(header, cancellationToken);
            await output.WriteAsync(body, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async ValueTask<int> ReadHeaderAsync(
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int length = 0;
        while (length < MaximumHeaderBytes)
        {
            int read = await input.ReadAsync(buffer.AsMemory(length, 1), cancellationToken);
            if (read == 0)
            {
                if (length == 0) return 0;
                throw new DapProtocolException("The debug adapter closed during a DAP header.");
            }
            length++;
            if (length >= HeaderTerminator.Length &&
                buffer.AsSpan(length - HeaderTerminator.Length, HeaderTerminator.Length)
                    .SequenceEqual(HeaderTerminator))
            {
                return length - HeaderTerminator.Length;
            }
        }
        throw new DapProtocolException("A DAP header exceeds its size limit.");
    }

    private static int ParseContentLength(ReadOnlySpan<byte> header)
    {
        if (header.ContainsAnyExceptInRange((byte)0, (byte)127))
            throw new DapProtocolException("A DAP header is not valid ASCII.");
        string text = Encoding.ASCII.GetString(header);

        int? contentLength = null;
        foreach (string line in text.Split("\r\n", StringSplitOptions.None))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0)
                throw new DapProtocolException("A DAP header line is malformed.");
            string name = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (contentLength is not null ||
                    !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                        out int parsed) || parsed <= 0 || parsed > MaximumMessageBytes)
                {
                    throw new DapProtocolException("A DAP Content-Length header is invalid.");
                }
                contentLength = parsed;
            }
            else if (!name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                throw new DapProtocolException("A DAP header name is unsupported.");
            }
        }
        return contentLength ?? throw new DapProtocolException(
            "A DAP Content-Length header is required.");
    }

    private async ValueTask ReadExactlyAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = await input.ReadAsync(destination[offset..], cancellationToken);
            if (read == 0)
                throw new DapProtocolException("The debug adapter closed during a DAP message.");
            offset += read;
        }
    }
}
