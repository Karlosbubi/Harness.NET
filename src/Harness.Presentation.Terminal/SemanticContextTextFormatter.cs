using Harness.BusinessLogic.Retrieval;

namespace Harness.Presentation.Terminal;

internal static class SemanticContextTextFormatter
{
    internal static string Format(SemanticIndexResult result) => string.Join(
        "\n",
        result.Error is null ? "State: ready" : $"Error: {result.Error}",
        $"Tracked files: {result.TrackedFileCount}",
        $"Skipped files: {result.SkippedFileCount}",
        $"Truncated: {result.IsTruncated}",
        $"Indexed files: {result.Partition?.FileCount ?? 0}",
        $"Chunks: {result.Partition?.ChunkCount ?? 0}",
        $"Embedding input tokens: {result.Usage.InputTokens}",
        $"Embedding cost: {FormatCost(result.Usage)}");

    internal static string Format(SemanticSearchResult result) => string.Join(
        "\n",
        result.Error is null ? $"Matches: {result.Matches.Count}" : $"Error: {result.Error}",
        $"Embedding input tokens: {result.Usage.InputTokens}",
        $"Embedding cost: {FormatCost(result.Usage)}",
        string.Empty,
        result.Matches.Count == 0
            ? "No context matches."
            : string.Join("\n\n", result.Matches.Select((match, index) =>
                $"{index + 1}. {match.Path}:{match.StartLine}-{match.EndLine} " +
                $"| distance {match.Distance.Value:F6}\n{match.Content}")));

    private static string FormatCost(EmbeddingUsageView usage) => usage.Cost is null
        ? "$0.000000"
        : $"${usage.Cost.Value / 1_000_000m:F6}";
}
