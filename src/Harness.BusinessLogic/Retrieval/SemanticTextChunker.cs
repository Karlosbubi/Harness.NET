using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.SemanticIndex;

namespace Harness.BusinessLogic.Retrieval;

internal static class SemanticTextChunker
{
    private const int MaximumChunkCharacters = 1600;
    private const int MinimumSplitCharacters = 800;
    private const int OverlapCharacters = 200;

    internal static IReadOnlyList<SemanticTextChunk> Chunk(TrackedTextDocument document)
    {
        string content = document.Content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        List<SemanticTextChunk> chunks = [];
        int start = 0;
        while (start < content.Length)
        {
            int end = Math.Min(start + MaximumChunkCharacters, content.Length);
            if (end < content.Length)
            {
                int newline = content.LastIndexOf(
                    '\n',
                    end - 1,
                    end - start - MinimumSplitCharacters + 1);
                if (newline >= start + MinimumSplitCharacters)
                {
                    end = newline + 1;
                }
            }

            string chunkContent = content[start..end].TrimEnd('\n');
            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                int startLine = 1 + CountNewlines(content.AsSpan(0, start));
                int endLine = startLine + CountNewlines(chunkContent.AsSpan());
                string hash = Hash(chunkContent);
                chunks.Add(new(
                    Hash($"{document.Path}\0{start}\0{end}\0{hash}"),
                    document.Path,
                    startLine,
                    endLine,
                    chunkContent,
                    hash));
            }

            if (end >= content.Length)
            {
                break;
            }

            int overlapStart = Math.Max(start + 1, end - OverlapCharacters);
            int nextNewline = content.IndexOf('\n', overlapStart, end - overlapStart);
            start = nextNewline >= 0 ? nextNewline + 1 : overlapStart;
        }

        return chunks;
    }

    private static int CountNewlines(ReadOnlySpan<char> value)
    {
        int count = 0;
        foreach (char character in value)
        {
            count += character == '\n' ? 1 : 0;
        }

        return count;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
