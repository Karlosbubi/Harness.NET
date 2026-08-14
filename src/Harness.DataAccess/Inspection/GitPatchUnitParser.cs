using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Harness.DataAccess.Inspection;

internal static class GitPatchUnitParser
{
    private const int MaximumUnits = 2_000;
    private const int MaximumPreviewCharacters = 240;

    internal static IReadOnlyList<DeveloperGitPatchUnit> Parse(
        string stagedDiff,
        string unstagedDiff,
        string fingerprint,
        bool stagedDiffTruncated,
        bool unstagedDiffTruncated)
    {
        var units = new List<DeveloperGitPatchUnit>();
        if (!unstagedDiffTruncated)
            AddDiff(units, unstagedDiff, fingerprint, DeveloperGitPatchDirection.Stage);
        if (!stagedDiffTruncated)
            AddDiff(units, stagedDiff, fingerprint, DeveloperGitPatchDirection.Unstage);
        return units;
    }

    private static void AddDiff(
        List<DeveloperGitPatchUnit> units,
        string diff,
        string fingerprint,
        DeveloperGitPatchDirection direction)
    {
        if (string.IsNullOrWhiteSpace(diff) || units.Count >= MaximumUnits) return;
        string[] lines = SplitLines(diff);
        int fileStart = -1;
        for (int index = 0; index <= lines.Length; index++)
        {
            bool boundary = index == lines.Length || lines[index].StartsWith("diff --git ", StringComparison.Ordinal);
            if (!boundary) continue;
            if (fileStart >= 0) AddFile(units, lines[fileStart..index], fingerprint, direction);
            fileStart = index < lines.Length ? index : -1;
        }
    }

    private static void AddFile(
        List<DeveloperGitPatchUnit> units,
        string[] fileLines,
        string fingerprint,
        DeveloperGitPatchDirection direction)
    {
        int firstHunk = Array.FindIndex(fileLines, line => line.StartsWith("@@ ", StringComparison.Ordinal));
        if (firstHunk < 0) return;
        string? path = ParsePath(fileLines[..firstHunk]);
        if (path is null) return;
        string header = string.Concat(fileLines[..firstHunk]);
        bool supportsLines = !header.Contains("new file mode ", StringComparison.Ordinal) &&
                             !header.Contains("deleted file mode ", StringComparison.Ordinal) &&
                             !header.Contains("rename from ", StringComparison.Ordinal) &&
                             !header.Contains("copy from ", StringComparison.Ordinal);
        for (int start = firstHunk; start < fileLines.Length && units.Count < MaximumUnits;)
        {
            int end = start + 1;
            while (end < fileLines.Length && !fileLines[end].StartsWith("@@ ", StringComparison.Ordinal)) end++;
            string[] hunk = fileLines[start..end];
            if (!TryParseHeader(hunk[0], out HunkRange range))
            {
                start = end;
                continue;
            }
            if (!IsComplete(hunk, range))
            {
                start = end;
                continue;
            }

            string patch = header + string.Concat(hunk);
            units.Add(CreateUnit(fingerprint, path, direction, DeveloperGitPatchKind.Hunk,
                hunk[0].TrimEnd(), range.OldStart, range.NewStart, string.Concat(hunk), patch,
                direction == DeveloperGitPatchDirection.Unstage));
            if (supportsLines) AddLines(units, header, hunk, range, fingerprint, path, direction);
            start = end;
        }
    }

    private static bool IsComplete(IReadOnlyList<string> hunk, HunkRange range)
    {
        int oldCount = 0;
        int newCount = 0;
        for (int index = 1; index < hunk.Count; index++)
        {
            char prefix = hunk[index].Length == 0 ? ' ' : hunk[index][0];
            if (prefix is ' ' or '-') oldCount++;
            if (prefix is ' ' or '+') newCount++;
        }
        return oldCount == range.OldCount && newCount == range.NewCount;
    }

    private static void AddLines(
        List<DeveloperGitPatchUnit> units,
        string header,
        string[] hunk,
        HunkRange range,
        string fingerprint,
        string path,
        DeveloperGitPatchDirection direction)
    {
        int oldLine = range.OldStart;
        int newLine = range.NewStart;
        for (int index = 1; index < hunk.Length && units.Count < MaximumUnits; index++)
        {
            string line = hunk[index];
            char prefix = line.Length == 0 ? ' ' : line[0];
            if (prefix is '-' or '+')
            {
                int? selectedOld = prefix == '-' ? oldLine : null;
                int? selectedNew = prefix == '+' ? newLine : null;
                string partial = BuildLinePatch(header, hunk, range, index, direction);
                string label = prefix == '-'
                    ? $"Remove line {oldLine.ToString(CultureInfo.InvariantCulture)}"
                    : $"Add line {newLine.ToString(CultureInfo.InvariantCulture)}";
                units.Add(CreateUnit(fingerprint, path, direction, DeveloperGitPatchKind.Line,
                    label, selectedOld, selectedNew, line, partial, ApplyInReverse: false));
            }

            if (prefix is ' ' or '-') oldLine++;
            if (prefix is ' ' or '+') newLine++;
        }
    }

    private static string BuildLinePatch(
        string header,
        string[] hunk,
        HunkRange range,
        int selectedIndex,
        DeveloperGitPatchDirection direction)
    {
        var body = new List<string>();
        int oldCount = 0;
        int newCount = 0;
        bool previousIncluded = false;
        for (int index = 1; index < hunk.Length; index++)
        {
            string line = hunk[index];
            char prefix = line.Length == 0 ? ' ' : line[0];
            bool selected = index == selectedIndex;
            string? output = direction == DeveloperGitPatchDirection.Stage
                ? StageLine(line, prefix, selected)
                : UnstageLine(line, prefix, selected);
            if (prefix == '\\' && !previousIncluded) output = null;
            previousIncluded = output is not null;
            if (output is null) continue;
            body.Add(output);
            char outputPrefix = output.Length == 0 ? ' ' : output[0];
            if (outputPrefix is ' ' or '-') oldCount++;
            if (outputPrefix is ' ' or '+') newCount++;
        }

        int oldStart = direction == DeveloperGitPatchDirection.Stage ? range.OldStart : range.NewStart;
        int newStart = direction == DeveloperGitPatchDirection.Stage ? range.NewStart : range.OldStart;
        string minimalHeader = MinimalHeader(header, direction == DeveloperGitPatchDirection.Unstage);
        return minimalHeader + $"@@ -{Range(oldStart, oldCount)} +{Range(newStart, newCount)} @@\n" +
               string.Concat(body);
    }

    private static string? StageLine(string line, char prefix, bool selected) => prefix switch
    {
        ' ' or '\\' => line,
        '-' => selected ? line : " " + line[1..],
        '+' => selected ? line : null,
        _ => null,
    };

    private static string? UnstageLine(string line, char prefix, bool selected) => prefix switch
    {
        ' ' or '\\' => line,
        '+' => selected ? "-" + line[1..] : " " + line[1..],
        '-' => selected ? "+" + line[1..] : null,
        _ => null,
    };

    private static string MinimalHeader(string header, bool reversePaths)
    {
        string[] lines = SplitLines(header);
        string diff = lines.First(line => line.StartsWith("diff --git ", StringComparison.Ordinal));
        string oldPath = lines.First(line => line.StartsWith("--- ", StringComparison.Ordinal));
        string newPath = lines.First(line => line.StartsWith("+++ ", StringComparison.Ordinal));
        return reversePaths ? diff + newPath.Replace("+++ ", "--- ", StringComparison.Ordinal) +
                              oldPath.Replace("--- ", "+++ ", StringComparison.Ordinal)
            : diff + oldPath + newPath;
    }

    private static DeveloperGitPatchUnit CreateUnit(
        string fingerprint,
        string path,
        DeveloperGitPatchDirection direction,
        DeveloperGitPatchKind kind,
        string label,
        int? oldLine,
        int? newLine,
        string preview,
        string patch,
        bool ApplyInReverse)
    {
        string identity = $"{fingerprint}\0{direction}\0{kind}\0{path}\0{patch}\0{ApplyInReverse}";
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        string boundedPreview = preview.Length <= MaximumPreviewCharacters
            ? preview.TrimEnd()
            : preview[..MaximumPreviewCharacters].TrimEnd() + "…";
        return new(id, new(path), direction, kind, label, oldLine, newLine,
            boundedPreview, patch, ApplyInReverse);
    }

    private static string? ParsePath(IReadOnlyList<string> header)
    {
        string? value = header.FirstOrDefault(line => line.StartsWith("+++ ", StringComparison.Ordinal));
        if (value is null || value.StartsWith("+++ /dev/null", StringComparison.Ordinal))
            value = header.FirstOrDefault(line => line.StartsWith("--- ", StringComparison.Ordinal));
        if (value is null) return null;
        value = value[4..].TrimEnd('\r', '\n');
        value = DecodeGitPath(value);
        if (value.StartsWith("a/", StringComparison.Ordinal) || value.StartsWith("b/", StringComparison.Ordinal))
            value = value[2..];
        return value == "/dev/null" || value.Length == 0 ? null : value;
    }

    private static string DecodeGitPath(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return value;
        using var bytes = new MemoryStream(value.Length);
        for (int index = 1; index < value.Length - 1; index++)
        {
            char character = value[index];
            if (character != '\\' || index + 1 >= value.Length - 1)
            {
                bytes.Write(Encoding.UTF8.GetBytes([character]));
                continue;
            }

            char escaped = value[++index];
            if (escaped is >= '0' and <= '7' && index + 2 < value.Length - 1 &&
                value[index + 1] is >= '0' and <= '7' && value[index + 2] is >= '0' and <= '7')
            {
                int octet = (escaped - '0') * 64 + (value[index + 1] - '0') * 8 + value[index + 2] - '0';
                bytes.WriteByte((byte)octet);
                index += 2;
                continue;
            }

            bytes.WriteByte(escaped switch
            {
                'a' => 0x07,
                'b' => 0x08,
                't' => 0x09,
                'n' => 0x0a,
                'v' => 0x0b,
                'f' => 0x0c,
                'r' => 0x0d,
                _ => (byte)escaped,
            });
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool TryParseHeader(string header, out HunkRange range)
    {
        range = default;
        int minus = header.IndexOf('-', StringComparison.Ordinal);
        int plus = header.IndexOf('+', StringComparison.Ordinal);
        int end = header.IndexOf(" @@", StringComparison.Ordinal);
        if (minus < 0 || plus <= minus || end <= plus) return false;
        return TryParseRange(header[(minus + 1)..plus].Trim(), out int oldStart, out int oldCount) &&
               TryParseRange(header[(plus + 1)..end].Trim(), out int newStart, out int newCount) &&
               Assign(out range, new(oldStart, oldCount, newStart, newCount));
    }

    private static bool TryParseRange(string value, out int start, out int count)
    {
        count = 0;
        string[] parts = value.Split(',', 2);
        return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out start) &&
               (parts.Length == 1
                   ? Assign(out count, 1)
                   : int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out count));
    }

    private static string Range(int start, int count) =>
        count == 1 ? start.ToString(CultureInfo.InvariantCulture) :
        $"{start.ToString(CultureInfo.InvariantCulture)},{count.ToString(CultureInfo.InvariantCulture)}";

    private static string[] SplitLines(string value)
    {
        var lines = new List<string>();
        int start = 0;
        while (start < value.Length)
        {
            int newline = value.IndexOf('\n', start);
            if (newline < 0)
            {
                lines.Add(value[start..]);
                break;
            }

            lines.Add(value[start..(newline + 1)]);
            start = newline + 1;
        }
        return lines.ToArray();
    }

    private static bool Assign<T>(out T target, T value)
    {
        target = value;
        return true;
    }

    private readonly record struct HunkRange(int OldStart, int OldCount, int NewStart, int NewCount);
}
