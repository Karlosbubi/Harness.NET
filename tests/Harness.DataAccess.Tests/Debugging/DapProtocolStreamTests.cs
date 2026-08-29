using System.Text;
using System.Text.Json;
using Harness.DataAccess.Debugging;

namespace Harness.DataAccess.Tests.Debugging;

public sealed class DapProtocolStreamTests
{
    [Fact]
    public async Task Reads_fragmented_content_length_frames()
    {
        byte[] body = "{\"seq\":1,\"type\":\"event\",\"event\":\"initialized\"}"u8.ToArray();
        byte[] frame = [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];
        using ChunkedReadStream input = new(frame, maximumChunk: 2);
        using MemoryStream output = new();
        DapProtocolStream protocol = new(input, output);

        using JsonDocument? message = await protocol.ReadAsync();

        Assert.NotNull(message);
        Assert.Equal("initialized", message.RootElement.GetProperty("event").GetString());
        Assert.Null(await protocol.ReadAsync());
    }

    [Fact]
    public async Task Writes_one_exact_utf8_frame()
    {
        using MemoryStream input = new();
        using MemoryStream output = new();
        DapProtocolStream protocol = new(input, output);

        await protocol.WriteAsync(new
        {
            seq = 4,
            type = "request",
            command = "threads",
            arguments = new { },
        });

        string frame = Encoding.UTF8.GetString(output.ToArray());
        Assert.StartsWith("Content-Length: ", frame, StringComparison.Ordinal);
        Assert.EndsWith(
            "{\"seq\":4,\"type\":\"request\",\"command\":\"threads\",\"arguments\":{}}",
            frame, StringComparison.Ordinal);
        int separator = frame.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int declared = int.Parse(frame["Content-Length: ".Length..separator],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Encoding.UTF8.GetByteCount(frame[(separator + 4)..]), declared);
    }

    [Theory]
    [InlineData("Content-Length: 0\r\n\r\n")]
    [InlineData("Content-Length: 5\r\nContent-Length: 5\r\n\r\nabcde")]
    [InlineData("X-Length: 2\r\n\r\n{}")]
    [InlineData("Content-Length: 2\n\n{}")]
    public async Task Rejects_malformed_or_ambiguous_headers(string frame)
    {
        using MemoryStream input = new(Encoding.ASCII.GetBytes(frame));
        using MemoryStream output = new();
        DapProtocolStream protocol = new(input, output);

        await Assert.ThrowsAsync<DapProtocolException>(async () =>
            await protocol.ReadAsync());
    }

    [Fact]
    public async Task Rejects_truncated_or_non_object_json_bodies()
    {
        await AssertRejectedAsync("Content-Length: 10\r\n\r\n{}");
        await AssertRejectedAsync("Content-Length: 2\r\n\r\n[]");
    }

    [Fact]
    public async Task Enforces_header_and_message_limits_before_allocation()
    {
        string oversizedHeader = new('A', DapProtocolStream.MaximumHeaderBytes);
        await AssertRejectedAsync(oversizedHeader);
        await AssertRejectedAsync(
            $"Content-Length: {DapProtocolStream.MaximumMessageBytes + 1}\r\n\r\n");
    }

    [Fact]
    public async Task Rejects_non_ascii_header_bytes()
    {
        byte[] frame = [.. "Content-Lengt"u8.ToArray(), 0xFF, .. ": 2\r\n\r\n{}"u8.ToArray()];
        using MemoryStream input = new(frame);
        using MemoryStream output = new();

        await Assert.ThrowsAsync<DapProtocolException>(async () =>
            await new DapProtocolStream(input, output).ReadAsync());
    }

    private static async Task AssertRejectedAsync(string frame)
    {
        using MemoryStream input = new(Encoding.ASCII.GetBytes(frame));
        using MemoryStream output = new();
        DapProtocolStream protocol = new(input, output);
        await Assert.ThrowsAsync<DapProtocolException>(async () =>
            await protocol.ReadAsync());
    }

    private sealed class ChunkedReadStream(byte[] bytes, int maximumChunk) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumChunk)], cancellationToken);
    }
}
