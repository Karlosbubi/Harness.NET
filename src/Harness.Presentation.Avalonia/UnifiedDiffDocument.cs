using System.Globalization;

namespace Harness.Presentation.Avalonia;

internal enum DiffLineKind
{
    Meta,
    FileHeader,
    HunkHeader,
    Context,
    Added,
    Removed,
}

/// <summary>One rendered row of a unified diff, with the line numbers it occupies.</summary>
internal sealed record DiffLine(DiffLineKind Kind, string Text, int? OldLine, int? NewLine);

/// <summary>
/// One side-by-side row. A modified region pairs removed lines with added lines; an
/// unbalanced region leaves the shorter side empty so both columns stay aligned.
/// </summary>
internal sealed record DiffRow(DiffLine? Left, DiffLine? Right);

/// <summary>
/// A unified diff parsed for display only. Harness.NET receives the diff as bounded text
/// from Data Access; this type decides how to decorate it, not what it means.
/// </summary>
internal sealed record UnifiedDiffDocument(
    IReadOnlyList<DiffLine> Lines,
    int AddedCount,
    int RemovedCount,
    int FileCount)
{
    internal static UnifiedDiffDocument Empty { get; } = new([], 0, 0, 0);

    internal bool IsEmpty => Lines.Count == 0;

    internal string Summary => IsEmpty
        ? "No textual changes."
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{FileCount} file(s) · +{AddedCount} −{RemovedCount}");

    internal static UnifiedDiffDocument Parse(string? diff)
    {
        if (string.IsNullOrEmpty(diff))
        {
            return Empty;
        }

        List<DiffLine> lines = [];
        int added = 0;
        int removed = 0;
        int files = 0;
        int oldLine = 0;
        int newLine = 0;

        foreach (string raw in diff.ReplaceLineEndings("\n").Split('\n'))
        {
            if (raw.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                files++;
                lines.Add(new(DiffLineKind.FileHeader, raw, null, null));
            }
            else if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                (oldLine, newLine) = ParseHunkStart(raw);
                lines.Add(new(DiffLineKind.HunkHeader, raw, null, null));
            }
            else if (IsMeta(raw))
            {
                lines.Add(new(DiffLineKind.Meta, raw, null, null));
            }
            else if (raw.StartsWith('+'))
            {
                added++;
                lines.Add(new(DiffLineKind.Added, raw[1..], null, newLine++));
            }
            else if (raw.StartsWith('-'))
            {
                removed++;
                lines.Add(new(DiffLineKind.Removed, raw[1..], oldLine++, null));
            }
            else
            {
                // A context line keeps its leading space; a trailing empty split entry is skipped.
                string text = raw.StartsWith(' ') ? raw[1..] : raw;
                lines.Add(new(DiffLineKind.Context, text, oldLine++, newLine++));
            }
        }

        TrimTrailingBlankContext(lines);
        return new(lines, added, removed, files);
    }

    /// <summary>Pairs the parsed lines into aligned left/right rows for comparison view.</summary>
    internal IReadOnlyList<DiffRow> ToSideBySideRows()
    {
        List<DiffRow> rows = [];
        List<DiffLine> pendingRemoved = [];
        List<DiffLine> pendingAdded = [];

        void FlushPending()
        {
            int paired = Math.Max(pendingRemoved.Count, pendingAdded.Count);
            for (int index = 0; index < paired; index++)
            {
                rows.Add(new(
                    index < pendingRemoved.Count ? pendingRemoved[index] : null,
                    index < pendingAdded.Count ? pendingAdded[index] : null));
            }

            pendingRemoved.Clear();
            pendingAdded.Clear();
        }

        foreach (DiffLine line in Lines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Removed:
                    pendingRemoved.Add(line);
                    break;
                case DiffLineKind.Added:
                    pendingAdded.Add(line);
                    break;
                default:
                    FlushPending();
                    rows.Add(new(line, line));
                    break;
            }
        }

        FlushPending();
        return rows;
    }

    private static bool IsMeta(string raw) =>
        raw.StartsWith("index ", StringComparison.Ordinal) ||
        raw.StartsWith("--- ", StringComparison.Ordinal) ||
        raw.StartsWith("+++ ", StringComparison.Ordinal) ||
        raw.StartsWith("new file mode ", StringComparison.Ordinal) ||
        raw.StartsWith("deleted file mode ", StringComparison.Ordinal) ||
        raw.StartsWith("old mode ", StringComparison.Ordinal) ||
        raw.StartsWith("new mode ", StringComparison.Ordinal) ||
        raw.StartsWith("similarity index ", StringComparison.Ordinal) ||
        raw.StartsWith("rename from ", StringComparison.Ordinal) ||
        raw.StartsWith("rename to ", StringComparison.Ordinal) ||
        raw.StartsWith("copy from ", StringComparison.Ordinal) ||
        raw.StartsWith("copy to ", StringComparison.Ordinal) ||
        raw.StartsWith("Binary files ", StringComparison.Ordinal) ||
        raw.StartsWith("GIT binary patch", StringComparison.Ordinal) ||
        raw.StartsWith(@"\ No newline", StringComparison.Ordinal);

    /// <summary>Splitting a trailing newline yields one empty entry that is not a real line.</summary>
    private static void TrimTrailingBlankContext(List<DiffLine> lines)
    {
        if (lines.Count > 0 &&
            lines[^1] is { Kind: DiffLineKind.Context, Text.Length: 0 })
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static (int Old, int New) ParseHunkStart(string header)
    {
        // @@ -oldStart[,oldCount] +newStart[,newCount] @@ optional section heading
        int minus = header.IndexOf('-');
        int plus = header.IndexOf('+');
        return (ParseStart(header, minus), ParseStart(header, plus));
    }

    private static int ParseStart(string header, int marker)
    {
        if (marker < 0)
        {
            return 1;
        }

        int index = marker + 1;
        int start = index;
        while (index < header.Length && char.IsAsciiDigit(header[index]))
        {
            index++;
        }

        return index > start &&
               int.TryParse(
                   header.AsSpan(start, index - start),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out int value)
            ? value
            : 1;
    }
}
